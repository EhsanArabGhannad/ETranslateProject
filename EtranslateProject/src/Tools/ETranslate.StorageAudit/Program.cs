using ETranslate.StorageAudit;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) => { args.Cancel = true; cancellation.Cancel(); };
return await AuditCommand.RunAsync(args, Console.Out, Console.Error, cancellation.Token);
