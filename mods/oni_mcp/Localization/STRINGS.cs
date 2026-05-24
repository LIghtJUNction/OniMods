namespace OniMcp
{
    public static class STRINGS
    {
        public static class MOD
        {
            public static LocString TITLE = "ONI MCP Server";
            public static LocString DESCRIPTION = "Oxygen Not Included MCP Server - exposes game state and actions through Model Context Protocol.";
        }

        public static class OPTIONS
        {
            public static class CATEGORIES
            {
                public static LocString SERVER = "Server";
                public static LocString SCREENSHOTS = "Screenshots";
            }

            public static class HOST
            {
                public static LocString NAME = "Host";
                public static LocString TOOLTIP = "HTTP listen host. Use localhost for local-only access, or 0.0.0.0 to listen on all interfaces.";
            }

            public static class PORT
            {
                public static LocString NAME = "Port";
                public static LocString TOOLTIP = "HTTP listen port for the MCP endpoint.";
            }

            public static class SCREENSHOT_CLEANUP_ENABLED
            {
                public static LocString NAME = "Auto-clean screenshots";
                public static LocString TOOLTIP = "Automatically delete old temporary screenshots created by MCP camera tools.";
            }

            public static class SCREENSHOT_RETENTION_MINUTES
            {
                public static LocString NAME = "Screenshot retention minutes";
                public static LocString TOOLTIP = "Delete MCP temporary screenshots older than this many minutes.";
            }

            public static class SCREENSHOT_MAX_FILES
            {
                public static LocString NAME = "Maximum screenshots";
                public static LocString TOOLTIP = "Keep at most this many MCP temporary screenshots.";
            }

        }
    }
}
