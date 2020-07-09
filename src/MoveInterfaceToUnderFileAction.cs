namespace IvySola.QuickActions
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using JetBrains.Application.DataContext;
    using JetBrains.Application.Interop.NativeHook;
    using JetBrains.Application.Progress;
    using JetBrains.Application.UI.Actions.ActionManager;
    using JetBrains.Application.UI.PopupLayout;
    using JetBrains.Lifetimes;
    using JetBrains.ProjectModel;
    using JetBrains.ProjectModel.DataContext;
    using JetBrains.ReSharper.Feature.Services.Bulbs;
    using JetBrains.ReSharper.Feature.Services.ContextActions;
    using JetBrains.ReSharper.Feature.Services.CSharp.Analyses.Bulbs;
    using JetBrains.ReSharper.Feature.Services.Intentions;
    using JetBrains.ReSharper.Feature.Services.Intentions.Scoped;
    using JetBrains.ReSharper.Feature.Services.Intentions.Scoped.Actions;
    using JetBrains.ReSharper.Feature.Services.Intentions.Scoped.Scopes;
    using JetBrains.ReSharper.Feature.Services.Refactorings;
    using JetBrains.ReSharper.Intentions.CSharp.ContextActions;
    using JetBrains.ReSharper.Psi;
    using JetBrains.ReSharper.Psi.CSharp.Tree;
    using JetBrains.ReSharper.Refactorings.Move.Impl;
    using JetBrains.ReSharper.Refactorings.Move.MoveIntoMatchingFile;
    using JetBrains.ReSharper.Refactorings.Move.MoveTypeDeclarationToFile;
    using JetBrains.ReSharper.Resources.Shell;
    using JetBrains.TextControl;
    using JetBrains.Util;

    public interface IX
    {

    }
    public class X : IX
    {

    }

    [ContextAction(
        Group = "C#",
        Name = "Move type to another file to match its name", 
        Priority = 2, 
        Description = "Moves current interface to under current file.")]
    public class MoveInterfaceToUnderFileAction : IBulbAction, IContextAction, IIndependentScopedAction, IScopedAction
    {
        private readonly ICSharpContextActionDataProvider _dataProvider;
        private string myProposedFileName;

        public MoveInterfaceToUnderFileAction(ICSharpContextActionDataProvider dataProvider) => _dataProvider = dataProvider;

        public string Text => $"Move to '{this.myProposedFileName}.cs'";

        public string ScopedText => "Move types to matching files";


        public FileCollectorInfo FileCollectorInfo => FileCollectorInfo.Default;

        public bool IsAvailable(IUserDataHolder cache)
        {
            var selectedElement = this._dataProvider.GetSelectedElement<ICSharpTypeDeclaration>(true, false);

            if (selectedElement == null || !selectedElement.IsValid())
                return false;
            if (selectedElement.GetContainingTypeDeclaration() != null)
                return false;

            if (!(selectedElement is IInterfaceDeclaration))
                return false;
            var nameRange = selectedElement.GetNameRange();
            var selectedTreeRange = this._dataProvider.SelectedTreeRange;
            if (!nameRange.Contains(selectedTreeRange))
            {
                return false;
            }
            var declaredElement = selectedElement.DeclaredElement;

            if (declaredElement == null)
                return false;
            if (RenameFileToMatchTypeNameAction.CountTopLevelTypeDeclarations(this._dataProvider.PsiFile) <= 1)
                return false;
            var fileName = Path.GetFileNameWithoutExtension(this._dataProvider.Document.Moniker);
            this.myProposedFileName = $"{fileName}.interface";
            return RenameFileToMatchTypeNameAction.TypeNameNameDoesNotCorrespondWithFileName(declaredElement, this._dataProvider.SourceFile.ToProjectFile());
        }

        public void Execute(ISolution solution, ITextControl textControl)
        {
            using (ReadLockCookie.Create())
            {
                var files = solution.GetPsiServices().Files;
                files.AssertAllDocumentAreCommitted(null);
                var selectedElement = this._dataProvider.GetSelectedElement<ICSharpTypeDeclaration>(true, false);
                var declaredElement = selectedElement?.DeclaredElement;
                if (declaredElement == null) return;

                var shortname = $"{Path.GetFileNameWithoutExtension(this._dataProvider.Document.Moniker)}.interface";
                var projectFile = this._dataProvider.SourceFile.ToProjectFile();
                if (projectFile == null) 
                    return;
                var fileName = shortname + projectFile.Location.ExtensionWithDot;
                var dataProvider = new MoveToFileDataProvider(fileName, selectedElement, true);
                Lifetime.Using(delegate(Lifetime lifetime)
                {
                    var instance = Shell.Instance;
                    var component = instance.GetComponent<IWindowsHookManager>();
                    var moveToFileWorkflow = new MoveToFileWorkflow(this._dataProvider.Solution, null, Shell.Instance.GetComponent<IMainWindowPopupWindowContext>());
                    moveToFileWorkflow.SetDataProvider(dataProvider);
                    var component2 = instance.GetComponent<IActionManager>();
                    var datarulesAdditional = DataRules.AddRule<ISolution>("ManualMoveTypeToAnotherFile", ProjectModelDataConstants.SOLUTION, this._dataProvider.Solution);
                    var context = component2.DataContexts.CreateOnSelection(lifetime, datarulesAdditional);
                    RefactoringActionUtil.ExecuteRefactoring(context, moveToFileWorkflow);
                });
            }
        }

        public Action<ITextControl> ExecuteAction(ISolution solution, Scope scope, IProgressIndicator progress)
        {
            return delegate(ITextControl textControl)
            {
                using (ReadLockCookie.Create())
                {
                    var files = solution.GetPsiServices().Files;
                    files.AssertAllDocumentAreCommitted(null);
                    var conflicts = new List<string>();
                    var dataProvider = new MoveIntoMatchingFilesDataProvider(true, true, true, true, conflicts);
                    Lifetime.Using(delegate(Lifetime lifetime)
                    {
                        var instance = Shell.Instance;
                        var moveIntoMatchingFilesWorkflow = new MoveIntoMatchingFilesWorkflow(this._dataProvider.Solution, null);
                        moveIntoMatchingFilesWorkflow.SetDataProvider(dataProvider);
                        var component = instance.GetComponent<IActionManager>();
                        var datarulesAdditional = DataRules.AddRule<ISolution>("ManualMoveTypesToMatchingFiles", ProjectModelDataConstants.SOLUTION, this._dataProvider.Solution).AddRule("ManualMoveTypesToMatchingFiles", ProjectModelDataConstants.PROJECT_MODEL_ELEMENTS, scope.GetProjectModelElementsDataConstant());
                        var context = component.DataContexts.CreateOnSelection(lifetime, datarulesAdditional);
                        RefactoringActionUtil.ExecuteRefactoring(context, moveIntoMatchingFilesWorkflow);
                    });
                }
            };
        }
        public IEnumerable<IntentionAction> CreateBulbItems() 
            => this.ToContextActionIntentions(null, null);
    }
}