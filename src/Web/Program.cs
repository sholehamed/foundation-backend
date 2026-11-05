using Scalar.ClientGeneration;
using Server;

var builder = WebApplication.CreateBuilder(args);


builder.RegisterServices();
var app = builder.Build();

app.RegisterApps(builder.Configuration);



app.Run();

