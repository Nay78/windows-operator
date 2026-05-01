using WindowsOperator.Agent.Hosting;

var app = OperatorApp.Build(args);
await app.RunAsync();
