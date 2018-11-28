using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;
using static N_Tools.Shared.Shared;
using System.Collections.Generic;
using EnvDTE;
using Microsoft.CodeAnalysis;
using System.Linq;
using Microsoft.CodeAnalysis.MSBuild;
using EnvDTE80;

namespace N_Tools.AuthAuditorCommand
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class AuthAuditorCommand
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("408ba7b3-8c3c-4f2a-a10e-5b05dca6aeff");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly AsyncPackage package;

        /// <summary>
        /// Initializes a new instance of the <see cref="AuthAuditorCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private AuthAuditorCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static AuthAuditorCommand Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
        {
            get
            {
                return this.package;
            }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            // Switch to the main thread - the call to AddCommand in AuthAuditorCommand's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync((typeof(IMenuCommandService))) as OleMenuCommandService;
            Instance = new AuthAuditorCommand(package, commandService);
        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            MSBuildWorkspace workspace = MSBuildWorkspace.Create();

            var targetProject = GetSelectedProject();
            string projectPath = null;
            string projectName = null;

            for (var i = 1; i <= targetProject.Properties.Count; i++)
            {
                if (targetProject.Properties.Item(i).Name == "FullPath")
                    projectPath = targetProject.Properties.Item(i).Value.ToString();
                if (targetProject.Properties.Item(i).Name == "FileName")
                    projectName = targetProject.Properties.Item(i).Value.ToString();
            }

            Microsoft.CodeAnalysis.Project currentProject = workspace.OpenProjectAsync(projectPath + projectName).Result;
            var projectCompilation = currentProject.GetCompilationAsync().Result;
            var assemblyTypes = projectCompilation.Assembly.TypeNames;
            var namedSymbols = assemblyTypes.Select(type => (INamedTypeSymbol)projectCompilation.GetSymbolsWithName(symbolName => symbolName == type).Where(symbol => symbol.Kind == SymbolKind.NamedType).First());
            var controllerSymbols = namedSymbols.Where(symbol => CheckTypeIsOrInheritedFromController(symbol)).ToList();

            var controllerMethodsCollection = new Dictionary<INamedTypeSymbol, IEnumerable<IMethodSymbol>>();

            foreach (var controller in controllerSymbols)
            {
                var isAuthorized = CheckControllerIsOrInheritedFromAuthorizedController(controller);
                var controllerActionMethods = controller.GetMembers()
                    .Where(member => member.DeclaredAccessibility == Accessibility.Public && member.Kind == SymbolKind.Method && ((IMethodSymbol)member).MethodKind == MethodKind.Ordinary)
                        .Select(item => (IMethodSymbol)item).ToList();
                var anonymousMethods = isAuthorized
                    ? controllerActionMethods.Where(action => action.GetAttributes().Any(attribute => attribute.AttributeClass.Name == "AllowAnonymousAttribute"))
                    : controllerActionMethods.Where(action => action.GetAttributes().All(attribute => attribute.AttributeClass.Name != "AuthorizeAttribute"));

                if (anonymousMethods.Count() > 0)
                    controllerMethodsCollection.Add(controller, anonymousMethods);
            }

            DTE2 dte = (DTE2)ServiceProvider.GetServiceAsync(typeof(DTE)).Result;
            OutputWindowPanes panes = dte.ToolWindows.OutputWindow.OutputWindowPanes;
            OutputWindowPane outputPane;
            try
            {
                outputPane = panes.Item("N-Tools");
            }
            catch (ArgumentException)
            {
                outputPane = panes.Add("N-Tools");
            }
            outputPane.Activate();
            foreach (var record in controllerMethodsCollection)
            {
                outputPane.OutputString("Controller: " + record.Key.Name);
                foreach (var method in record.Value)
                {
                    outputPane.OutputString(Environment.NewLine);
                    outputPane.OutputString("\t");
                    outputPane.OutputString("Action: ");
                    outputPane.OutputString(method.Name);
                    outputPane.OutputString(Environment.NewLine);
                }
                outputPane.OutputString(Environment.NewLine);
            }
        }

        private bool CheckTypeIsOrInheritedFromController(INamedTypeSymbol typeSymbol)
        {
            if (typeSymbol.GetAttributes().Any(attribute => attribute.AttributeClass.Name == "ApiControllerAttribute"))
                return true;
            if (typeSymbol.BaseType != null)
            {
                var result = false;
                if (typeSymbol.BaseType.Name == "ControllerBase")
                    result = true;
                else
                    result = CheckTypeIsOrInheritedFromController(typeSymbol.BaseType);

                return result;
            }
            else
                return false;
        }

        private bool CheckControllerIsOrInheritedFromAuthorizedController(INamedTypeSymbol typeSymbol)
        {
            if (typeSymbol.GetAttributes().Any(attribute => attribute.AttributeClass.Name == "AuthorizeAttribute"))
                return true;
            if (typeSymbol.GetAttributes().All(attribute => attribute.AttributeClass.Name != "AuthorizeAttribute")
                && typeSymbol.BaseType == null)
                return false;
            return CheckControllerIsOrInheritedFromAuthorizedController(typeSymbol.BaseType);
        }
    }
}
