namespace Sherlock.MCP.Tests.Ambiguity.Alpha
{
    public class DuplicateWidget
    {
        public void Spin() { }
    }
}

namespace Sherlock.MCP.Tests.Ambiguity.Beta
{
    public class DuplicateWidget
    {
        public void Spin() { }
    }

    public class DuplicateCASING { }

    public class DuplicateCasing { }
}
