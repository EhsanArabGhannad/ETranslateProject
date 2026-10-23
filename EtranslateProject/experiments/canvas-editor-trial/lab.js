import Editor, { EditorMode, RowFlex } from '@hufe921/canvas-editor';
import { sample, flat, canonicalText, htmlFixture, rtlSentence } from './samples.js';
import { isRichDocument } from '../../src/Web/ETranslate.Web/ClientApp/document-schema.js';
async function start() {
    const find=id=>document.getElementById(id), mm=v=>v*96/25.4;
    let editor, selected='rtl', source='', changedAt=performance.now(), changes=0, pending, issue=null;
    let drawCalls=[];
    // Local observation only: record the vendor's text drawing without altering its arguments.
    const originalFill=CanvasRenderingContext2D.prototype.fillText;
    CanvasRenderingContext2D.prototype.fillText=function(value,x,y,...rest) {
        if(this.canvas.closest('#editor') && drawCalls.length<12000) drawCalls.push({text:String(value),x,y,font:this.font,direction:this.direction,page:[...find('editor').querySelectorAll('canvas')].indexOf(this.canvas)+1});
        return originalFill.call(this,value,x,y,...rest);
    };
    const options={width:mm(210),height:mm(297),margins:[mm(25),mm(20),mm(28),mm(20)],defaultFont:'LabNoto, LabArabic',defaultSize:16,
        defaultRowMargin:1,defaultBasicRowMarginHeight:8,wordBreak:'break-word',pageGap:28,locale:'en',scrollContainerSelector:'#paper-scroll',
        header:{top:25,editable:false},footer:{bottom:25,editable:false},pageNumber:{bottom:12,format:'{pageNo}/{pageCount}'},
        accessibility:{disabled:true},ruler:{disabled:true},spellcheck:{disabled:true}};
    const data=()=>editor.command.getValue().data;
    const text=()=>canonicalText(flat(data().main));
    const pages=()=>[...find('editor').querySelectorAll('canvas[data-index]')];
    function report() {
        const value=editor.command.getValue();
        const arabic=drawCalls.filter(c=>/[\u0600-\u06ff]/.test(c.text));
        return {engine:'@hufe921/canvas-editor@1.0.5',sample:selected,pages:pages().length,layoutIssue:issue,
            configuredFont:editor.command.getOptions().defaultFont,
            sourceTextPreserved:text()===source,productionCompatible:isRichDocument(value),changes,
            millisecondsSinceChange:performance.now()-changedAt,
            arabicDrawCallCount:arabic.length,arabicDrawSamples:arabic.slice(0,12),
            scope:'Synthetic local-only canvas trial. Native schema is not the production Tiptap schema. No real Word clipboard or DOCX test.'};
    }
    function refresh(){if(!editor)return;const r=report();find('status').textContent=`${r.pages} sayfa | Deney belgesi | Üretim biçimiyle uyumlu değil`;
        find('issues').hidden=!issue;find('issues').textContent=issue?'Deney sınırı aşıldı; gerçek belge kullanmayın. Bu örnek onaylanmadı.':'';
        find('report').textContent=JSON.stringify(r,null,2);find('page-nav').replaceChildren();
        pages().forEach((canvas,i)=>{const b=document.createElement('button');b.type='button';b.textContent=String(i+1);b.setAttribute('aria-label',`Sayfa ${i+1}`);b.addEventListener('click',()=>canvas.scrollIntoView({block:'nearest'}));find('page-nav').append(b);});}
    function touch(){changes++;changedAt=performance.now();clearTimeout(pending);pending=setTimeout(refresh,100);}
    function mount(value){editor?.destroy();find('editor').replaceChildren();drawCalls=[];changes=0;issue=null;editor=new Editor(find('editor'),structuredClone(value),options);
        editor.override.paste=()=>({preventDefault:true});editor.override.drop=()=>({preventDefault:true});editor.override.pasteImage=()=>({preventDefault:true});
        editor.listener.contentChange=touch;editor.listener.pageSizeChange=()=>{changedAt=performance.now();refresh();};
        // Establish a public-API history baseline even before the first pointer/cursor event.
        editor.command.executeForceUpdate({isSubmitHistory:true});
        changedAt=performance.now();find('zoom').value='1';}
    async function settle(){await new Promise(requestAnimationFrame);await new Promise(requestAnimationFrame);refresh();}
    async function load(id){selected=id;find('sample').value=id;
        if(id==='html'){mount({main:[{value:''}]});editor.command.executeSetHTML({header:'',main:htmlFixture,footer:''});source=text();}
        else{const value=sample(id);source=canonicalText(flat(value.main));mount(value);}
        await settle();find('paper-scroll').scrollTop=0;return report();}
    function cursors(word){const ranges=editor.command.getKeywordRangeList(word);if(!ranges.length)return [];
        const r=ranges[0],saved=editor.command.getRange(),results=[];
        for(let i=r.startIndex+1;i<=r.endIndex;i++){editor.command.executeSetRange(i,i);const c=editor.command.getCursorPosition();results.push({index:i,value:c?.value,x:c?.coordinate.leftTop[0],y:c?.coordinate.leftTop[1],page:c?.pageNo});}
        editor.command.executeReplaceRange(saved);return results;}
    find('load').addEventListener('click',()=>load(find('sample').value));
    find('reload').addEventListener('click',async()=>{const value=data();mount(value);await settle();});
    find('zoom').addEventListener('change',()=>{editor.command.executePageScale(Number(find('zoom').value));changedAt=performance.now();refresh();});
    for(const b of document.querySelectorAll('[data-cmd]')){b.addEventListener('mousedown',e=>e.preventDefault());b.addEventListener('click',()=>{({bold:()=>editor.command.executeBold(),italic:()=>editor.command.executeItalic(),right:()=>editor.command.executeRowFlex(RowFlex.RIGHT),left:()=>editor.command.executeRowFlex(RowFlex.LEFT),undo:()=>editor.command.executeUndo(),redo:()=>editor.command.executeRedo()})[b.dataset.cmd]();refresh();});}
    document.addEventListener('paste',e=>{e.preventDefault();e.stopImmediatePropagation();},true);document.addEventListener('drop',e=>{e.preventDefault();e.stopImmediatePropagation();},true);
    // Keep upload/clipboard/link actions from vendor context menus outside this synthetic trial.
    document.addEventListener('contextmenu',e=>{e.preventDefault();e.stopImmediatePropagation();},true);
    await Promise.all(['LabNoto','LabArabic'].flatMap(f=>[document.fonts.load(`16px ${f}`),document.fonts.load(`bold 16px ${f}`)]));
    await load('rtl');
    window.canvasTrial={load,report,data,text,range:()=>editor.command.getRange(),cursor:()=>editor.command.getCursorPosition(),rtlCursors:()=>cursors('سلام'),
        focusKeyword:(word)=>{const r=editor.command.getKeywordRangeList(word)[0];if(!r)return false;editor.command.executeFocus({range:{...r,startIndex:r.endIndex},isMoveCursorToVisible:true});return true;},
        undo:()=>editor.command.executeUndo(),redo:()=>editor.command.executeRedo(),
        prepend:(value)=>editor.command.executeAppendElementList(value,{isPrepend:true,isSubmitHistory:true}),
        roundTrip:async()=>{const value=data();mount(value);await settle();return report();},
        images:async()=>{drawCalls=[];return await editor.command.getImage({pixelRatio:1,mode:EditorMode.PRINT});},
        paintedText:()=>drawCalls.map(c=>c.text).join(''),html:()=>editor.command.getHTML(),
        pageData:(pageNo)=>editor.command.getValue({pageNo}).data,
        rtlReference:rtlSentence,forceDraw:()=>{drawCalls=[];editor.command.executeForceUpdate({isSubmitHistory:false});return report();}};
}
start().catch(error=>{document.getElementById('status').textContent=`Deney başlatılamadı: ${error.message}`;console.error(error);});
