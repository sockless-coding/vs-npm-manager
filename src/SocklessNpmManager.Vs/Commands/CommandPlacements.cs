using System;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace SocklessNpmManager.Vs.Commands
{
    /// <summary>
    /// Solution Explorer context-menu anchors. These are the group ids Microsoft's
    /// CommandParentingSample uses — the shell command set (<c>guidSHLMainMenu</c>) plus the
    /// well-known context-menu group ids (not the menu ids).
    /// </summary>
    internal static class CommandPlacements
    {
        private static readonly Guid ShlMainMenu = new("d309f791-903f-11d0-9efc-00a0c911004f");

        /// <summary>Context menu shown when a project item is selected.</summary>
        public static CommandPlacement FileInProjectContextMenu => CommandPlacement.VsctParent(ShlMainMenu, id: 521, priority: 0x0100);

        /// <summary>Context menu shown when a project is selected.</summary>
        public static CommandPlacement ProjectContextMenu => CommandPlacement.VsctParent(ShlMainMenu, id: 518, priority: 0x0100);

        /// <summary>Context menu shown when the solution is selected.</summary>
        public static CommandPlacement SolutionContextMenu => CommandPlacement.VsctParent(ShlMainMenu, id: 537, priority: 0x0100);
    }
}
