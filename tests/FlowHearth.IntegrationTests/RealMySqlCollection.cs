namespace FlowHearth.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RealMySqlTestGroup
{
    public const string Name = "Real MySQL";
}
