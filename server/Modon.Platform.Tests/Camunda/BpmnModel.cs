using System.Xml.Linq;

namespace Modon.Platform.Tests.Camunda;

/// <summary>
/// A tiny interpreter for the BPMN elements the excavation process uses (start/end events, exclusive merge
/// gateways, an event-based gateway and message catch events). It reads the checked-in BPMN, so the fake
/// Camunda in the tests follows the real model rather than a hand-written copy of it.
/// </summary>
public sealed class BpmnModel
{
    private static readonly XNamespace Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly XNamespace Zeebe = "http://camunda.org/schema/zeebe/1.0";

    public sealed record Element(string Id, string Type, string? MessageName, string[] Outgoing);

    public string ProcessId { get; }
    public string? VersionTag { get; }
    public IReadOnlyDictionary<string, Element> Elements { get; }

    /// <summary>Message name → its subscription's correlation key expression.</summary>
    public IReadOnlyDictionary<string, string?> Messages { get; }

    public BpmnModel(string xml)
    {
        var doc = XDocument.Parse(xml);
        var messages = doc.Root!.Elements(Bpmn + "message").ToDictionary(
            m => (string)m.Attribute("id")!,
            m => ((string)m.Attribute("name")!, (string?)m.Descendants(Zeebe + "subscription").FirstOrDefault()?.Attribute("correlationKey")));
        Messages = messages.Values.ToDictionary(m => m.Item1, m => m.Item2);

        var process = doc.Root.Elements(Bpmn + "process").Single();
        ProcessId = (string)process.Attribute("id")!;
        VersionTag = (string?)process.Descendants(Zeebe + "versionTag").FirstOrDefault()?.Attribute("value");

        var flows = process.Elements(Bpmn + "sequenceFlow")
            .ToLookup(f => (string)f.Attribute("sourceRef")!, f => (string)f.Attribute("targetRef")!);
        Elements = process.Elements()
            .Where(e => e.Name.LocalName is not ("sequenceFlow" or "extensionElements"))
            .Select(e =>
            {
                var id = (string)e.Attribute("id")!;
                var messageRef = (string?)e.Element(Bpmn + "messageEventDefinition")?.Attribute("messageRef");
                return new Element(id, TypeOf(e.Name.LocalName), messageRef is null ? null : messages[messageRef].Item1, flows[id].ToArray());
            })
            .ToDictionary(e => e.Id);
    }

    public static BpmnModel Load() => new(Modon.Platform.Requests.Workflow.Camunda.ExcavationProcess.LoadBpmn());

    /// <summary>Camunda's element type names, as the search API returns them.</summary>
    private static string TypeOf(string localName) => localName switch
    {
        "startEvent" => "START_EVENT",
        "endEvent" => "END_EVENT",
        "exclusiveGateway" => "EXCLUSIVE_GATEWAY",
        "eventBasedGateway" => "EVENT_BASED_GATEWAY",
        "intermediateCatchEvent" => "INTERMEDIATE_CATCH_EVENT",
        _ => throw new NotSupportedException($"BPMN element <{localName}> is not supported by the test interpreter."),
    };

    public string StartEventId => Elements.Values.Single(e => e.Type == "START_EVENT").Id;

    /// <summary>
    /// Follows sequence flows from <paramref name="fromId"/> (which has just completed) until the token waits
    /// (event-based gateway, message catch event) or ends. Returns the visited elements, the last one being
    /// where the token now is.
    /// </summary>
    public List<Element> RunFrom(string fromId)
    {
        var visited = new List<Element>();
        var current = Elements[fromId];
        while (true)
        {
            if (current.Outgoing.Length != 1)
                throw new InvalidOperationException($"{current.Id} must have exactly one outgoing flow here.");
            current = Elements[current.Outgoing[0]];
            visited.Add(current);
            if (current.Type is "EVENT_BASED_GATEWAY" or "INTERMEDIATE_CATCH_EVENT" or "END_EVENT") return visited;
        }
    }

    /// <summary>Messages the token accepts while waiting at <paramref name="waitId"/>, with the catch event each one completes.</summary>
    public IReadOnlyDictionary<string, Element> AcceptedMessages(string waitId)
    {
        var wait = Elements[waitId];
        return wait.Type switch
        {
            "EVENT_BASED_GATEWAY" => wait.Outgoing.Select(id => Elements[id]).ToDictionary(e => e.MessageName!),
            "INTERMEDIATE_CATCH_EVENT" => new Dictionary<string, Element> { [wait.MessageName!] = wait },
            _ => new Dictionary<string, Element>(),
        };
    }
}
