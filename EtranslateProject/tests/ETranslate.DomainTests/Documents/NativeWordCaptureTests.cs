using System.Text.Json;
using ETranslate.Contracts.Documents;
using Microsoft.AspNetCore.Http;

namespace ETranslate.DomainTests.Documents;

public sealed partial class EditorAdapterBrowserTests
{
    private sealed class LocalNativeWordCaptureFact : FactAttribute
    {
        public LocalNativeWordCaptureFact() { if (Environment.GetEnvironmentVariable("ETRANSLATE_NATIVE_WORD_CAPTURE") != "1") Skip = "Interactive native Word clipboard capture is opt-in."; }
    }

    [LocalNativeWordCaptureFact]
    public async Task Native_Word_clipboard_capture_host_only_for_synthetic_local_documents()
    {
        var root = Path.GetFullPath(Environment.GetEnvironmentVariable("ETRANSLATE_EDITOR_ADAPTER_WEB_ROOT")!);
        var directory = Path.GetFullPath(Path.Combine(root, "../../../tmp/native-word-capture")); Directory.CreateDirectory(directory);
        var cases = new Dictionary<string, string> { ["typography"] = "RW_TYPO_START", ["tables"] = "RW_TABLE_START", ["unsupported"] = "RW_UNSUPPORTED_START" };
        var captured = cases.Keys.Where(name => File.Exists(Path.Combine(directory, name + ".capture.json"))).ToHashSet(); var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const string empty = "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\"}]}";
        await using var fixture = await BrowserFixture.CreateAsync(empty, headed: true); fixture.Format = DocumentContentDescriptor.RichFormat; fixture.SchemaVersion = "10"; fixture.WordCaptureEnabled = true;
        fixture.ExtraHandler = async context => {
            var path = context.Request.Path.Value;
            if (context.Request.Method == "GET" && path == "/Workspace/Editor") { if (!context.Request.Query.ContainsKey("after-save")) fixture.Source = empty; return false; }
            if (context.Request.Method == "GET" && path == "/qa/capture.js") {
                context.Response.ContentType = "text/javascript"; await context.Response.WriteAsync(CaptureScript); return true;
            }
            if (context.Request.Method == "POST" && path == "/Workspace/SaveDraft") {
                var form = await context.Request.ReadFormAsync(); var source = form["editorContentJson"].ToString();
                if (!DocumentContentDescriptor.RichV10.TryRead(source, out _)) { context.Response.StatusCode = 400; return true; }
                fixture.Source = source; context.Response.StatusCode = 303; context.Response.Headers.Location = "/Workspace/Editor?after-save=1&sample=" + Uri.EscapeDataString(context.Request.Query["sample"].ToString()); return true;
            }
            if (context.Request.Method == "POST" && path == "/qa/capture") {
                using var body = await JsonDocument.ParseAsync(context.Request.Body); var value = body.RootElement;
                var name = value.GetProperty("sample").GetString() ?? ""; var text = value.GetProperty("text").GetString() ?? "";
                if (!cases.TryGetValue(name, out var marker) || !text.Contains(marker) || !value.GetProperty("trusted").GetBoolean()) { context.Response.StatusCode = 400; return true; }
                var html = value.GetProperty("html").GetString() ?? ""; var source = value.GetProperty("source").GetString() ?? "";
                if (html.Length > 2_000_000 || !DocumentContentDescriptor.RichV10.TryRead(source, out _)) { context.Response.StatusCode = 400; return true; }
                await File.WriteAllTextAsync(Path.Combine(directory, name + ".capture.json"), value.GetRawText()); captured.Add(name);
                context.Response.ContentType = "application/json"; await context.Response.WriteAsync("{\"recorded\":true}"); return true;
            }
            if (context.Request.Method == "POST" && path == "/qa/finish") {
                if (captured.Count != 3) { context.Response.StatusCode = 409; return true; }
                await context.Response.WriteAsync("Capture complete"); complete.TrySetResult(); return true;
            }
            return false;
        };
        await File.WriteAllTextAsync(Path.Combine(directory, "host.json"), JsonSerializer.Serialize(new { url = fixture.BaseUrl + "/Workspace/Editor?sample=typography", directory, syntheticOnly = true }));
        await fixture.OpenAsync();
        var deadline = DateTimeOffset.UtcNow.AddMinutes(45);
        while (!complete.Task.IsCompleted && DateTimeOffset.UtcNow < deadline) {
            var commandFile = Path.Combine(directory, "command.json");
            if (!File.Exists(commandFile)) { await Task.Delay(250); continue; }
            using var command = JsonDocument.Parse(await File.ReadAllTextAsync(commandFile)); File.Delete(commandFile);
            var op = command.RootElement.GetProperty("operation").GetString(); var sample = command.RootElement.GetProperty("sample").GetString() ?? "";
            if (!cases.ContainsKey(sample)) throw new InvalidOperationException("Unknown QA sample");
            if (op == "navigate") await fixture.Page.GotoAsync(fixture.BaseUrl + "/Workspace/Editor?sample=" + sample);
            else if (op == "paste") {
                await fixture.Page.Locator("#rich-text [role=textbox]").FocusAsync(); await fixture.Page.Keyboard.PressAsync("Control+v");
                await fixture.Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Record native Word capture", Exact = true }).ClickAsync();
                await Microsoft.Playwright.Assertions.Expect(fixture.Page.GetByText("Recorded native Word capture: " + sample, new() { Exact = true })).ToBeVisibleAsync();
                await fixture.Page.ScreenshotAsync(new() { Path = Path.Combine(directory, sample + ".native.png"), FullPage = true });
            } else if (op == "save") {
                var before = await fixture.Page.Locator("#document-json").InputValueAsync();
                await fixture.Page.Locator("#draft-form button[type=submit]").ClickAsync(); await fixture.Page.WaitForURLAsync("**after-save=1**");
                Assert.Equal(before, await fixture.Page.Locator("#document-json").InputValueAsync());
            } else if (op == "finish") complete.TrySetResult(); else throw new InvalidOperationException("Unknown QA operation");
            await File.WriteAllTextAsync(Path.Combine(directory, "ack.json"), JsonSerializer.Serialize(new { operation = op, sample, captures = captured.ToArray(), errors = fixture.Errors, unexpected = fixture.Unexpected }));
        }
        Assert.True(complete.Task.IsCompleted); Assert.Equal(3, captured.Count);
    }

    private const string CaptureScript = """
        (()=>{
            let last=null; const sample=new URLSearchParams(location.search).get('sample')||'typography';
            const panel=document.createElement('section'),title=document.createElement('h2'),note=document.createElement('p'),record=document.createElement('button'),finish=document.createElement('button');
            title.textContent='Native Word QA — '+sample;note.textContent='Paste only the generated Word QA document into the translation editor, then record.';
            record.type=finish.type='button';record.textContent='Record native Word capture';finish.textContent='Finish all captures';panel.append(title,note,record,finish);document.querySelector('main').prepend(panel);
            for(const name of ['typography','tables','unsupported']){const a=document.createElement('a');a.href='/Workspace/Editor?sample='+name;a.textContent=' '+name+' ';panel.append(a);}
            document.addEventListener('paste',event=>{if(event.target.closest?.('#rich-text'))last={trusted:event.isTrusted,html:event.clipboardData?.getData('text/html')||'',text:event.clipboardData?.getData('text/plain')||'',types:[...(event.clipboardData?.types||[])]};},true);
            record.addEventListener('click',async()=>{if(!last){note.textContent='No native paste observed';return;}const source=document.querySelector('#document-json').value;
                const response=await fetch('/qa/capture',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({...last,sample,source,status:document.querySelector('#editor-paste-status').textContent,browser:navigator.userAgent})});note.textContent=response.ok?'Recorded native Word capture: '+sample:'Capture rejected';});
            finish.addEventListener('click',async()=>{const response=await fetch('/qa/finish',{method:'POST'});note.textContent=await response.text();});
            const form=document.querySelector('#draft-form');form.action='/Workspace/SaveDraft?sample='+encodeURIComponent(sample);
        })();
        """;
}
