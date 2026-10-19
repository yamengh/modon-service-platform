using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modon.Platform.Requests.Workflow.Camunda;

namespace Modon.Platform.Tests.Camunda;

/// <summary>
/// The real backend with <c>Modon:Workflow:Engine = Camunda</c>. The Camunda adapter and SDK are the production
/// ones; only the SDK's HTTP handler is replaced by <see cref="FakeCamunda"/>, so no cluster is needed.
/// </summary>
public sealed class CamundaAppFactory : ModonAppFactory
{
    public FakeCamunda Camunda { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Modon:Workflow:Engine", "Camunda");
        builder.UseSetting("Modon:Workflow:Camunda:RestAddress", FakeCamunda.Address);
        builder.UseSetting("Modon:Workflow:Camunda:RequestTimeoutSeconds", "2");
        builder.ConfigureTestServices(services => services.AddSingleton<ICamundaEngine>(sp =>
            new CamundaSdkEngine(sp.GetRequiredService<WorkflowOptions>(), sp.GetRequiredService<ILoggerFactory>(), Camunda)));
    }
}

[CollectionDefinition("Camunda API")]
public class CamundaApiCollection : ICollectionFixture<CamundaAppFactory>;
