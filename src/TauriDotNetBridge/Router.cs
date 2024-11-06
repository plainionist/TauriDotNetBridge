using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using TauriDotNetBridge.Contracts;

namespace TauriDotNetBridge;

internal class Router
{
    private static readonly JsonSerializerSettings myResponseSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver()
    };

    private static readonly JsonSerializerSettings myRequestSettings = new()
    {
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new DefaultNamingStrategy()
        }
    };

    private readonly Composer myComposer;
    private readonly JsonSerializer mySerializer;
    private readonly ILogger<Router> myLogger;

    public Router(Composer composer)
    {
        myComposer = composer;

        myLogger = myComposer.ServiceProvider!.GetRequiredService<ILogger<Router>>();
        mySerializer = JsonSerializer.Create(myRequestSettings);
    }

    public string RouteRequest(string? requestText)
    {
        if (requestText is null or "")
        {
            return Serialize(RouteResponse.Error("Request string is empty"));
        }

        RouteRequest? request;
        try
        {
            request = JsonConvert.DeserializeObject<RouteRequest>(requestText, myRequestSettings);
            if (request == null)
            {
                return Serialize(RouteResponse.Error("Failed to parse request JSON"));
            }
        }
        catch (Exception)
        {
            return Serialize(RouteResponse.Error("Failed to parse request JSON"));
        }

        var response = RouteRequest(request.Controller, request.Action, request.Data);
        return Serialize(response);
    }

    private static string Serialize(RouteResponse response) =>
        JsonConvert.SerializeObject(response, myResponseSettings);

    public RouteResponse RouteRequest(string controller, string action, object? data)
    {
        var type = myComposer.Services.FirstOrDefault(x =>
            (x.ImplementationType?.Name.Equals(controller, StringComparison.OrdinalIgnoreCase) == true ||
             x.ImplementationType?.Name.Equals(controller + "Controller", StringComparison.OrdinalIgnoreCase) == true) &&
            x.ImplementationType?.IsClass == true &&
            x.ImplementationType?.IsAbstract == false)
            ?.ImplementationType;

        if (type == null)
        {
            myLogger.LogWarning($"No controller found for: '{controller}'");
            return RouteResponse.Error($"No controller found for: '{controller}'");
        }

        var method = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(x => x.Name.Equals(action, StringComparison.OrdinalIgnoreCase)
                                 && x.GetParameters().Length <= 1);

        if (method == null)
        {
            myLogger.LogWarning($"No action found for '{action}' in controller '{controller}'");
            return RouteResponse.Error($"No action found for '{action}' in controller '{controller}'");
        }

        var instance = myComposer.ServiceProvider!.GetService(type);
        if (instance == null)
        {
            myLogger.LogError($"Failed to resolve a controller instance for '{type}.{method}'");
            return RouteResponse.Error($"Failed to resolve a controller instance for '{type}.{method}'");
        }

        try
        {
            if (data is null)
            {
                return RouteResponse.Ok(method.Invoke(instance, null));
            }
            else
            {
                var arg = ((JObject)data).ToObject(method.GetParameters().Single().ParameterType, mySerializer);
                return RouteResponse.Ok(method.Invoke(instance, [arg]));
            }
        }
        catch (Exception ex)
        {
            return RouteResponse.Error(ex);
        }
    }
}
