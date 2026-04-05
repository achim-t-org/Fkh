using Microsoft.Extensions.Logging;

namespace FK8s.Services;

public class FK8sRemoveNode : FK8sServiceBase
{
    public FK8sRemoveNode(ILogger<FK8sRemoveNode> logger) : base(logger) { }

    public async Task<string> RemoveNodeAsync(Dictionary<string, string> parameters)
    {
        var nodeUrl = parameters["NodeUrl"];

        // TODO: implement node removal
        return "Hello World";
    }
}
