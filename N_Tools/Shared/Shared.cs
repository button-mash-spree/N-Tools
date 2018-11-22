using EnvDTE;
using EnvDTE80;
using Microsoft;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;

namespace N_Tools.Shared
{
    public class Shared
    {
        public static Project GetSelectedProject()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            DTE2 _applicationObject = (DTE2)ServiceProvider.GlobalProvider.GetService(typeof(SDTE));
            Assumes.Present(_applicationObject);
            UIHierarchy solutionExplorerHirarechy = _applicationObject.ToolWindows.SolutionExplorer;
            Array solutionExplorerSelectedItems = (Array)solutionExplorerHirarechy.SelectedItems;

            if (null != solutionExplorerSelectedItems)
            {
                Project selectedProject = ((UIHierarchyItem)solutionExplorerSelectedItems.GetValue(0)).Object as Project;
                return selectedProject;
            }
            return null;
        }
    }
}
