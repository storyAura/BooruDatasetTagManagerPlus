using BooruDatasetTagManager.AiApi;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static BooruDatasetTagManager.DatasetManager;

namespace BooruDatasetTagManager
{
    public sealed class Form_CharacterTagAuditWizard : Form
    {
        private const int PreviewMinimumWidth = 280;
        private const int ResultMinimumWidth = 420;
        private const int PreviewMaximumDimension = 2048;
        private readonly MainForm owner;
        private readonly TabControl pages = new TabControl();
        private readonly ComboBox comboTrigger = new ComboBox();
        private readonly ComboBox comboStyle = new ComboBox();
        private readonly ComboBox comboMode = new ComboBox();
        private readonly ComboBox comboSubjectMode = new ComboBox();
        private readonly ComboBox comboTriggerB = new ComboBox();
        private readonly ComboBox comboGenderA = new ComboBox();
        private readonly ComboBox comboGenderB = new ComboBox();
        private readonly ComboBox comboFolderA = new ComboBox();
        private readonly ComboBox comboFolderB = new ComboBox();
        private readonly Button buttonSetReferenceA = new Button();
        private readonly Button buttonSetReferenceB = new Button();
        private readonly Label labelReferenceA = new Label();
        private readonly Label labelReferenceB = new Label();
        private readonly ComboBox comboResultProfile = new ComboBox();
        private readonly TextBox textModel = new TextBox();
        private readonly NumericUpDown numericMinimumCount = new NumericUpDown();
        private readonly TextBox textImageSearch = new TextBox();
        private readonly ListView imageGallery = new ListView();
        private readonly ImageList galleryImages = new ImageList();
        private readonly ProgressBar progressBar = new ProgressBar();
        private readonly Label labelProgress = new Label();
        private readonly Label labelInitialSummary = new Label();
        private readonly ComboBox comboInitialFilter = new ComboBox();
        private readonly BufferedDataGridView initialGrid = new BufferedDataGridView();
        private readonly BufferedPictureBox referencePreview = new BufferedPictureBox();
        private readonly TextBox textTagSearch = new TextBox();
        private readonly CheckBox checkDeletesOnly = new CheckBox();
        private readonly Button buttonRedoVisual = new Button();
        private readonly BufferedDataGridView resultGrid = new BufferedDataGridView();
        private readonly Label labelSummary = new Label();
        private readonly SplitContainer resultSplit = new SplitContainer();
        private readonly Button buttonExcluded = new Button();
        private readonly BufferedDataGridView excludedGrid = new BufferedDataGridView();
        private readonly TextBox textFinalPrompt = new TextBox();
        private readonly Button buttonCopyPrompt = new Button();
        private readonly Label labelMetrics = new Label();
        private readonly ToolTip metricsToolTip = new ToolTip();
        private readonly Button buttonBack = new Button();
        private readonly Button buttonNext = new Button();
        private readonly Button buttonCancel = new Button();
        private CancellationTokenSource cancellation;
        // Deferred-close support: closing the wizard mid-audit/apply used to
        // dispose the form under the awaiting continuation, which then touched
        // disposed controls (ObjectDisposedException from an async void chain).
        private bool closeAfterWork;
        private bool applyInProgress;
        private readonly CancellationTokenSource translationCancellation = new CancellationTokenSource();
        private readonly SemaphoreSlim reasonTranslationSemaphore = new SemaphoreSlim(4, 4);
        private AbstractTranslator reasonTranslator;
        private CharacterTagReasonLocalizer reasonLocalizer;
        private List<ReviewRow> reviewRows = new List<ReviewRow>();
        private List<ReviewRow> initialRows = new List<ReviewRow>();
        private List<CharacterTagAuditItem> initialAuditItems = new List<CharacterTagAuditItem>();
        private CharacterTagAuditResult auditResult;
        // Dual-mode state: one result/review-row set per audited character.
        private List<CharacterTagAuditResult> dualResults;
        private List<List<ReviewRow>> dualReviewRows;
        private CharacterTagDualAuditResult dualAuditInfo;
        private List<CharacterAuditProfile> runProfiles;
        private int activeResultProfile;
        private bool dualRunActive;
        private Control[] dualModeControls = Array.Empty<Control>();
        private string selectedImagePathB;
        private string selectedImagePath;
        private bool loadingGallery;
        private bool excludedExpanded;
        private bool settingSplitter;
        private bool splitterAdjustedByUser;
        private bool splitterDragging;
        private DecisionChoice[] decisionChoices = Array.Empty<DecisionChoice>();
        private ComboBox activeDecisionCombo;
        private CancellationTokenSource galleryLoadCancellation;
        private int wizardPhase;

        public Form_CharacterTagAuditWizard(MainForm owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            InitializeComponent();
            ApplyLanguage();
            LoadDefaults();
            InitializeReasonLocalization();
            FormClosed += (_, _) =>
            {
                galleryLoadCancellation?.Cancel();
                galleryLoadCancellation?.Dispose();
                galleryLoadCancellation = null;
            };
            Shown += (_, _) => _ = LoadGalleryAsync();
            // The wizard is modeless: the user can switch the dataset's folder
            // scope in the main window while it is open. Refresh the gallery
            // when focus returns and the scoped image set changed.
            Activated += (_, _) => _ = ReloadGalleryIfScopeChangedAsync();
        }

        private void InitializeComponent()
        {
            Text = "Character tag audit";
            StartPosition = FormStartPosition.CenterParent;
            // Wide enough that the review page's rightmost column (加入 /
            // 复制提示词) can never be clipped out of reach.
            MinimumSize = new Size(1200, 680);
            ClientSize = new Size(1280, 800);
            ShowInTaskbar = false;

            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8)
            };
            buttonCancel.AutoSize = buttonBack.AutoSize = buttonNext.AutoSize = true;
            buttonCancel.MinimumSize = buttonBack.MinimumSize = buttonNext.MinimumSize = new Size(100, 30);
            buttonCancel.DialogResult = DialogResult.Cancel;
            buttonCancel.Click += ButtonCancel_Click;
            buttonBack.Visible = false;
            buttonBack.Enabled = false;
            buttonNext.Click += async (_, _) => await NextAsync();
            actions.Controls.AddRange(new Control[] { buttonCancel, buttonNext });
            Controls.Add(actions);
            AcceptButton = buttonNext;
            CancelButton = buttonCancel;
            FormClosing += (_, e) =>
            {
                if (cancellation != null || applyInProgress)
                {
                    // Defer the close until the running audit/apply unwinds so its
                    // continuations never run on a disposed form.
                    e.Cancel = true;
                    closeAfterWork = true;
                    cancellation?.Cancel();
                }
            };

            pages.Dock = DockStyle.Fill;
            pages.TabPages.Add(CreateSelectionPage());
            pages.TabPages.Add(CreateProgressPage());
            pages.TabPages.Add(CreateResultPage());
            pages.Selecting += PreventSelection;
            Controls.Add(pages);
            ShowPage(0);
        }

        private TabPage CreateSelectionPage()
        {
            TabPage page = new TabPage("1");
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), RowCount = 3, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            TableLayoutPanel settings = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, RowCount = 6 };
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            comboStyle.DropDownStyle = ComboBoxStyle.DropDownList;
            comboMode.DropDownStyle = ComboBoxStyle.DropDownList;
            comboSubjectMode.DropDownStyle = ComboBoxStyle.DropDownList;
            comboGenderA.DropDownStyle = ComboBoxStyle.DropDownList;
            comboGenderB.DropDownStyle = ComboBoxStyle.DropDownList;
            comboFolderA.DropDownStyle = ComboBoxStyle.DropDownList;
            comboFolderB.DropDownStyle = ComboBoxStyle.DropDownList;
            comboStyle.Dock = DockStyle.Fill;
            comboMode.Dock = DockStyle.Fill;
            comboSubjectMode.Dock = DockStyle.Fill;
            comboFolderA.Dock = DockStyle.Fill;
            comboFolderB.Dock = DockStyle.Fill;
            comboStyle.DropDown += (_, _) => UpdateChoiceDropDownWidth(comboStyle);
            comboMode.DropDown += (_, _) => UpdateChoiceDropDownWidth(comboMode);
            comboSubjectMode.DropDown += (_, _) => UpdateChoiceDropDownWidth(comboSubjectMode);
            comboSubjectMode.SelectedIndexChanged += (_, _) => UpdateSubjectModeVisibility();
            // Append (inline) completion only: Suggest opens a second native
            // popup that overlaps the combo's own dropdown list.
            comboTrigger.DropDownStyle = ComboBoxStyle.DropDown;
            comboTrigger.AutoCompleteMode = AutoCompleteMode.Append;
            comboTrigger.AutoCompleteSource = AutoCompleteSource.ListItems;
            comboTrigger.Dock = DockStyle.Fill;
            comboTriggerB.DropDownStyle = ComboBoxStyle.DropDown;
            comboTriggerB.AutoCompleteMode = AutoCompleteMode.Append;
            comboTriggerB.AutoCompleteSource = AutoCompleteSource.ListItems;
            comboTriggerB.Dock = DockStyle.Fill;
            textModel.ReadOnly = true;
            textModel.Dock = DockStyle.Fill;
            numericMinimumCount.Minimum = 1;
            numericMinimumCount.Maximum = 1000000;
            numericMinimumCount.Width = 120;
            settings.Controls.Add(new Label { Name = "labelTrigger", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            settings.Controls.Add(comboTrigger, 1, 0);
            settings.Controls.Add(new Label { Name = "labelAuditModel", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 0);
            settings.Controls.Add(textModel, 3, 0);
            settings.Controls.Add(new Label { Name = "labelAuditStyle", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            settings.Controls.Add(comboStyle, 1, 1);
            settings.Controls.Add(new Label { Name = "labelAuditMode", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 1);
            settings.Controls.Add(comboMode, 3, 1);
            settings.Controls.Add(new Label { Name = "labelMinimumCount", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
            settings.Controls.Add(numericMinimumCount, 1, 2);
            var labelSubjectMode = new Label { Name = "labelSubjectMode", AutoSize = true, Anchor = AnchorStyles.Left };
            settings.Controls.Add(labelSubjectMode, 2, 2);
            settings.Controls.Add(comboSubjectMode, 3, 2);

            // Dual-mode rows: second trigger, per-character genders, optional
            // repeat-folder scopes and the two reference image pickers.
            var labelTriggerB = new Label { Name = "labelTriggerB", AutoSize = true, Anchor = AnchorStyles.Left };
            var labelGenders = new Label { Name = "labelGenders", AutoSize = true, Anchor = AnchorStyles.Left };
            var labelFolderA = new Label { Name = "labelFolderA", AutoSize = true, Anchor = AnchorStyles.Left };
            var labelFolderB = new Label { Name = "labelFolderB", AutoSize = true, Anchor = AnchorStyles.Left };
            var labelReferences = new Label { Name = "labelReferences", AutoSize = true, Anchor = AnchorStyles.Left };
            FlowLayoutPanel genderFlow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            comboGenderA.Width = 110;
            comboGenderB.Width = 110;
            comboGenderB.Margin = new Padding(8, 3, 3, 3);
            genderFlow.Controls.Add(comboGenderA);
            genderFlow.Controls.Add(comboGenderB);
            FlowLayoutPanel referenceFlow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            buttonSetReferenceA.AutoSize = true;
            buttonSetReferenceB.AutoSize = true;
            buttonSetReferenceB.Margin = new Padding(16, 3, 3, 3);
            // Fixed width + ellipsis: kohya file names run hundreds of chars
            // and an AutoSize label stretched the whole settings row.
            labelReferenceA.AutoSize = false;
            labelReferenceB.AutoSize = false;
            labelReferenceA.AutoEllipsis = true;
            labelReferenceB.AutoEllipsis = true;
            labelReferenceA.TextAlign = ContentAlignment.MiddleLeft;
            labelReferenceB.TextAlign = ContentAlignment.MiddleLeft;
            labelReferenceA.Size = labelReferenceB.Size =
                new Size(LogicalToDeviceUnits(230), LogicalToDeviceUnits(26));
            labelReferenceA.Margin = labelReferenceB.Margin = new Padding(6, 6, 3, 3);
            buttonSetReferenceA.Click += (_, _) => AssignReferenceFromGallery(isProfileA: true);
            buttonSetReferenceB.Click += (_, _) => AssignReferenceFromGallery(isProfileA: false);
            referenceFlow.Controls.Add(buttonSetReferenceA);
            referenceFlow.Controls.Add(labelReferenceA);
            referenceFlow.Controls.Add(buttonSetReferenceB);
            referenceFlow.Controls.Add(labelReferenceB);
            settings.Controls.Add(labelTriggerB, 0, 3);
            settings.Controls.Add(comboTriggerB, 1, 3);
            settings.Controls.Add(labelGenders, 2, 3);
            settings.Controls.Add(genderFlow, 3, 3);
            settings.Controls.Add(labelFolderA, 0, 4);
            settings.Controls.Add(comboFolderA, 1, 4);
            settings.Controls.Add(labelFolderB, 2, 4);
            settings.Controls.Add(comboFolderB, 3, 4);
            settings.Controls.Add(labelReferences, 0, 5);
            settings.Controls.Add(referenceFlow, 1, 5);
            settings.SetColumnSpan(referenceFlow, 3);
            dualModeControls = new Control[]
            {
                labelTriggerB, comboTriggerB, labelGenders, genderFlow,
                labelFolderA, comboFolderA, labelFolderB, comboFolderB,
                labelReferences, referenceFlow
            };
            root.Controls.Add(settings, 0, 0);

            textImageSearch.Dock = DockStyle.Top;
            textImageSearch.Margin = new Padding(0, 8, 0, 8);
            textImageSearch.TextChanged += (_, _) => FilterGallery();
            root.Controls.Add(textImageSearch, 0, 1);

            galleryImages.ImageSize = new Size(128, 128);
            galleryImages.ColorDepth = ColorDepth.Depth32Bit;
            imageGallery.Dock = DockStyle.Fill;
            imageGallery.View = View.LargeIcon;
            imageGallery.MultiSelect = false;
            imageGallery.HideSelection = false;
            imageGallery.LargeImageList = galleryImages;
            root.Controls.Add(imageGallery, 0, 2);
            page.Controls.Add(root);
            return page;
        }

        private TabPage CreateProgressPage()
        {
            TabPage page = new TabPage("2");
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 4, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            labelProgress.Dock = DockStyle.Top;
            labelProgress.TextAlign = ContentAlignment.MiddleLeft;
            labelProgress.Font = new Font(Font, FontStyle.Bold);
            progressBar.Dock = DockStyle.Top;
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Minimum = 0;
            progressBar.Maximum = 2;
            progressBar.Value = 0;
            FlowLayoutPanel initialHeader = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
            labelInitialSummary.AutoSize = true;
            labelInitialSummary.Margin = new Padding(0, 6, 18, 0);
            comboInitialFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            comboInitialFilter.SelectedIndexChanged += (_, _) => FillInitialGrid();
            initialHeader.Controls.Add(labelInitialSummary);
            initialHeader.Controls.Add(comboInitialFilter);
            ConfigureReadOnlyAuditGrid(initialGrid, includeInitial: false);
            initialGrid.DataBindingComplete += (_, _) =>
            {
                ApplyDecisionRowColors(initialGrid);
                ApplyReasonTooltips(initialGrid);
            };
            AttachWikiContextMenu(initialGrid);
            layout.Controls.Add(labelProgress, 0, 0);
            layout.Controls.Add(progressBar, 0, 1);
            layout.Controls.Add(initialHeader, 0, 2);
            layout.Controls.Add(initialGrid, 0, 3);
            page.Controls.Add(layout);
            return page;
        }

        private TabPage CreateResultPage()
        {
            TabPage page = new TabPage("3");
            resultSplit.Dock = DockStyle.Fill;
            resultSplit.FixedPanel = FixedPanel.None;
            resultSplit.SplitterWidth = 7;
            resultSplit.SplitterMoved += (_, _) => { if (!settingSplitter) splitterAdjustedByUser = true; };
            resultSplit.SplitterMoving += (_, _) => BeginSplitterDrag();
            resultSplit.MouseUp += (_, _) => EndSplitterDrag();
            resultSplit.MouseCaptureChanged += (_, _) =>
            {
                if (!resultSplit.Capture)
                    EndSplitterDrag();
            };
            referencePreview.Dock = DockStyle.Fill;
            referencePreview.SizeMode = PictureBoxSizeMode.Zoom;
            referencePreview.BackColor = Color.Black;
            resultSplit.Panel1.Padding = new Padding(8);
            resultSplit.Panel1.Controls.Add(referencePreview);

            TableLayoutPanel right = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), RowCount = 7, ColumnCount = 1 };
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            FlowLayoutPanel filters = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
            comboResultProfile.DropDownStyle = ComboBoxStyle.DropDownList;
            comboResultProfile.Width = 240;
            comboResultProfile.Visible = false;
            comboResultProfile.Margin = new Padding(3, 0, 12, 0);
            comboResultProfile.DropDown += (_, _) => UpdateChoiceDropDownWidth(comboResultProfile);
            comboResultProfile.SelectedIndexChanged += (_, _) => SwitchResultProfile(comboResultProfile.SelectedIndex);
            textTagSearch.Width = 260;
            textTagSearch.TextChanged += (_, _) => FillResultGrid();
            checkDeletesOnly.AutoSize = true;
            checkDeletesOnly.CheckedChanged += (_, _) => FillResultGrid();
            buttonRedoVisual.AutoSize = true;
            buttonRedoVisual.Margin = new Padding(12, 0, 3, 0);
            buttonRedoVisual.Click += async (_, _) => await RedoVisualReviewAsync();
            filters.Controls.Add(comboResultProfile);
            filters.Controls.Add(textTagSearch);
            filters.Controls.Add(checkDeletesOnly);
            filters.Controls.Add(buttonRedoVisual);
            labelSummary.AutoSize = true;
            labelSummary.Margin = new Padding(3, 8, 3, 8);
            right.Controls.Add(filters, 0, 0);
            right.Controls.Add(labelSummary, 0, 1);

            resultGrid.Dock = DockStyle.Fill;
            resultGrid.AutoGenerateColumns = false;
            resultGrid.AllowUserToAddRows = false;
            resultGrid.AllowUserToDeleteRows = false;
            resultGrid.RowHeadersVisible = false;
            resultGrid.EditMode = DataGridViewEditMode.EditOnEnter;
            resultGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            var decisionColumn = new DataGridViewComboBoxColumn
            {
                DataPropertyName = nameof(ReviewRow.Decision),
                Name = "Decision",
                FillWeight = 75,
                ValueType = typeof(CharacterTagDecision),
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                FlatStyle = FlatStyle.Standard
            };
            resultGrid.Columns.Add(decisionColumn);
            resultGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ReviewRow.Tag), Name = "Tag", FillWeight = 120, ReadOnly = true });
            resultGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ReviewRow.Count), Name = "Count", FillWeight = 45, ReadOnly = true });
            resultGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ReviewRow.InitialDisplay), Name = "Initial", FillWeight = 70, ReadOnly = true });
            resultGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ReviewRow.ReplacementTag), Name = "Replacement", FillWeight = 110 });
            resultGrid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(ReviewRow.IncludeInPrompt), Name = "IncludePrompt", FillWeight = 55 });
            resultGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ReviewRow.CategoryDisplay), Name = "Category", FillWeight = 75, ReadOnly = true });
            resultGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ReviewRow.ReasonDisplay), Name = "Reason", FillWeight = 180, ReadOnly = true });
            resultGrid.DataBindingComplete += (_, _) =>
            {
                ApplyDecisionRowColors(resultGrid);
                ApplyCellProtection();
                ApplyReasonTooltips(resultGrid);
            };
            resultGrid.CellValueChanged += ResultGrid_CellValueChanged;
            resultGrid.EditingControlShowing += ResultGrid_EditingControlShowing;
            resultGrid.CellClick += ResultGrid_OpenDecisionDropdown;
            resultGrid.CurrentCellDirtyStateChanged += (_, _) =>
            {
                if (resultGrid.IsCurrentCellDirty)
                    resultGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            resultGrid.DataError += (_, e) => e.ThrowException = false;
            AttachWikiContextMenu(resultGrid);
            right.Controls.Add(resultGrid, 0, 2);

            TableLayoutPanel promptPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            promptPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            promptPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            textFinalPrompt.Dock = DockStyle.Fill;
            textFinalPrompt.Multiline = true;
            textFinalPrompt.ReadOnly = true;
            textFinalPrompt.ScrollBars = ScrollBars.Vertical;
            buttonCopyPrompt.AutoSize = true;
            buttonCopyPrompt.Dock = DockStyle.Fill;
            buttonCopyPrompt.Click += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(textFinalPrompt.Text))
                    Clipboard.SetText(textFinalPrompt.Text);
            };
            promptPanel.Controls.Add(textFinalPrompt, 0, 0);
            promptPanel.Controls.Add(buttonCopyPrompt, 1, 0);
            right.Controls.Add(promptPanel, 0, 3);
            labelMetrics.AutoSize = true;
            labelMetrics.Dock = DockStyle.Right;
            labelMetrics.TextAlign = ContentAlignment.MiddleRight;
            right.Controls.Add(labelMetrics, 0, 4);
            buttonExcluded.AutoSize = true;
            buttonExcluded.FlatStyle = FlatStyle.Flat;
            buttonExcluded.TextAlign = ContentAlignment.MiddleLeft;
            buttonExcluded.Click += (_, _) => ToggleExcluded();
            right.Controls.Add(buttonExcluded, 0, 5);
            ConfigureExcludedGrid();
            right.Controls.Add(excludedGrid, 0, 6);
            resultSplit.Panel2.Controls.Add(right);
            page.Controls.Add(resultSplit);
            page.Resize += (_, _) => UpdatePreviewWidth();
            return page;
        }

        private static void ConfigureReadOnlyAuditGrid(DataGridView grid, bool includeInitial)
        {
            grid.Dock = DockStyle.Fill;
            grid.AutoGenerateColumns = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.ReadOnly = true;
            grid.RowHeadersVisible = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ReviewRow.Tag), Name = "Tag", FillWeight = 120 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ReviewRow.Count), Name = "Count", FillWeight = 45 });
            if (includeInitial)
                grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ReviewRow.InitialDisplay), Name = "Initial", FillWeight = 65 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ReviewRow.FinalDisplay), Name = "Final", FillWeight = 65 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ReviewRow.ReplacementTag), Name = "Replacement", FillWeight = 100 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ReviewRow.CategoryDisplay), Name = "Category", FillWeight = 80 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ReviewRow.ReasonDisplay), Name = "Reason", FillWeight = 190 });
        }

        private static void ApplyAuditGridHeaders(DataGridView grid)
        {
            grid.Columns["Tag"].HeaderText = I18n.GetText("CharacterTagAuditTag");
            grid.Columns["Count"].HeaderText = I18n.GetText("GridCount");
            if (grid.Columns.Contains("Initial"))
                grid.Columns["Initial"].HeaderText = I18n.GetText("CharacterTagAuditInitial");
            grid.Columns["Final"].HeaderText = I18n.GetText("CharacterTagAuditDecision");
            grid.Columns["Replacement"].HeaderText = I18n.GetText("CharacterTagAuditReplacement");
            grid.Columns["Category"].HeaderText = I18n.GetText("CharacterTagAuditCategory");
            grid.Columns["Reason"].HeaderText = I18n.GetText("CharacterTagAuditReason");
        }

        private void InitializeReasonLocalization()
        {
            reasonTranslator = FallbackTranslator.Create(
                Program.Settings.GetTranslationProviderOrder(),
                Program.Settings.TranslationTimeoutSeconds);
            reasonLocalizer = new CharacterTagReasonLocalizer(
                Program.Settings.Language,
                async (text, sourceLanguage, targetLanguage, token) =>
                {
                    await reasonTranslationSemaphore.WaitAsync(token);
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        string translated = await reasonTranslator.TranslateAsync(text, sourceLanguage, targetLanguage);
                        token.ThrowIfCancellationRequested();
                        return translated;
                    }
                    finally
                    {
                        reasonTranslationSemaphore.Release();
                    }
                });
        }

        private void AttachWikiContextMenu(DataGridView grid)
        {
            var menu = new ContextMenuStrip();
            var query = new ToolStripMenuItem(I18n.GetText("TagContextQueryDanbooruWiki"));
            ReviewRow selectedRow = null;
            query.Click += (_, _) =>
            {
                ReviewRow item = selectedRow;
                if (item == null || string.IsNullOrWhiteSpace(item.Tag))
                    return;
                var popup = new Form_TagWikiPopup(item.Tag);
                popup.Show(this);
            };
            menu.Items.Add(query);
            grid.CellMouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count)
                    return;
                selectedRow = grid.Rows[e.RowIndex].DataBoundItem as ReviewRow;
                if (selectedRow == null)
                    return;
                if (e.ColumnIndex >= 0)
                    grid.CurrentCell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
                grid.ClearSelection();
                grid.Rows[e.RowIndex].Selected = true;
                menu.Show(Cursor.Position);
            };
        }

        private static void ApplyReasonTooltips(DataGridView grid)
        {
            foreach (DataGridViewRow dataRow in grid.Rows)
            {
                if (dataRow.DataBoundItem is not ReviewRow row || !grid.Columns.Contains("Reason"))
                    continue;
                string tooltip = row.OriginalReason ?? string.Empty;
                if (row.ReasonTranslationFailed)
                    tooltip += Environment.NewLine + I18n.GetText("CharacterTagAuditReasonTranslationFailed");
                dataRow.Cells["Reason"].ToolTipText = tooltip;
            }
        }

        private async Task LocalizeReasonsAsync(IReadOnlyList<ReviewRow> rows, DataGridView grid)
        {
            if (reasonLocalizer == null || rows.Count == 0)
                return;
            try
            {
                await Task.WhenAll(rows.Select(async row =>
                {
                    CharacterTagLocalizedReason localized = await reasonLocalizer.LocalizeAsync(
                        row.OriginalReason,
                        translationCancellation.Token);
                    row.ReasonDisplay = localized.Text;
                    row.ReasonTranslationFailed = localized.UsedFallback;
                }));
                if (!IsDisposed && IsHandleCreated)
                {
                    BeginInvoke(() =>
                    {
                        if (IsDisposed)
                            return;
                        if (grid.Columns.Contains("Reason"))
                            grid.InvalidateColumn(grid.Columns["Reason"].Index);
                        ApplyReasonTooltips(grid);
                    });
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static string LocalizeDecision(CharacterTagDecision decision)
        {
            return decision switch
            {
                CharacterTagDecision.Keep => I18n.GetText("CharacterTagDecisionKeep"),
                CharacterTagDecision.Delete => I18n.GetText("CharacterTagDecisionDelete"),
                CharacterTagDecision.Replace => I18n.GetText("CharacterTagDecisionReplace"),
                _ => I18n.GetText("CharacterTagDecisionUncertain")
            };
        }

        private ReviewRow CreateReviewRow(CharacterTagAuditItem item, string trigger, bool includeInitial)
        {
            string originalReason = item.Reason ?? string.Empty;
            return new ReviewRow
            {
                Tag = item.Tag,
                Count = item.Count,
                InitialValue = item.InitialDecision,
                InitialDisplay = includeInitial ? LocalizeDecision(item.InitialDecision) : string.Empty,
                FinalDisplay = LocalizeDecision(item.FinalDecision),
                CategoryValue = item.Category,
                CategoryDisplay = I18n.GetText(CharacterTagCategoryLocalization.GetKey(item.Category)),
                OriginalReason = originalReason,
                ReasonDisplay = reasonLocalizer != null && reasonLocalizer.RequiresTranslation
                    ? I18n.GetText("CharacterTagAuditReasonTranslating")
                    : originalReason,
                // Every row except the locked trigger word is user-editable; the
                // category policy only constrains the AI, not manual review.
                CanModify = !string.Equals(item.Tag, trigger, StringComparison.Ordinal),
                Decision = string.Equals(item.Tag, trigger, StringComparison.Ordinal)
                    ? CharacterTagDecision.Keep
                    : item.FinalDecision,
                ReplacementTag = item.ReplacementTag,
                IncludeInPrompt = item.IncludeInPrompt,
                PromptOrder = item.PromptOrder
            };
        }

        private void ConfigureExcludedGrid()
        {
            excludedGrid.Dock = DockStyle.Fill;
            excludedGrid.AutoGenerateColumns = false;
            excludedGrid.AllowUserToAddRows = false;
            excludedGrid.AllowUserToDeleteRows = false;
            excludedGrid.ReadOnly = true;
            excludedGrid.RowHeadersVisible = false;
            excludedGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            excludedGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(CharacterTagInventoryItem.Tag), Name = "Tag", FillWeight = 150 });
            excludedGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(CharacterTagInventoryItem.Count), Name = "Count", FillWeight = 50 });
        }

        private void ToggleExcluded()
        {
            excludedExpanded = !excludedExpanded;
            TableLayoutPanel parent = (TableLayoutPanel)excludedGrid.Parent;
            parent.RowStyles[6].Height = excludedExpanded ? 150 : 0;
            excludedGrid.Visible = excludedExpanded;
            UpdateExcludedButtonText();
        }

        private void UpdateExcludedButtonText()
        {
            int count = auditResult?.ExcludedItems.Count ?? 0;
            buttonExcluded.Text = (excludedExpanded ? "▼ " : "▶ ")
                + string.Format(I18n.GetText("CharacterTagAuditExcludedHeader"), count);
        }

        private void UpdatePreviewWidth()
        {
            if (resultSplit.Width <= PreviewMinimumWidth + ResultMinimumWidth + resultSplit.SplitterWidth)
                return;
            EnsureSplitterMinimums();
            if (splitterAdjustedByUser || referencePreview.Image == null)
                return;
            int width = CharacterTagPreviewLayout.CalculateWidth(
                resultSplit.Width,
                Math.Max(1, resultSplit.Height - 16),
                referencePreview.Image.Width,
                referencePreview.Image.Height);
            int maximum = Math.Max(PreviewMinimumWidth, resultSplit.Width - ResultMinimumWidth - resultSplit.SplitterWidth);
            settingSplitter = true;
            try
            {
                resultSplit.SplitterDistance = Math.Clamp(width, PreviewMinimumWidth, maximum);
            }
            finally
            {
                settingSplitter = false;
            }
        }

        private void EnsureSplitterMinimums()
        {
            if (resultSplit.Panel1MinSize == PreviewMinimumWidth
                && resultSplit.Panel2MinSize == ResultMinimumWidth)
            {
                return;
            }
            int maximum = resultSplit.Width - ResultMinimumWidth - resultSplit.SplitterWidth;
            settingSplitter = true;
            try
            {
                resultSplit.Panel1MinSize = 0;
                resultSplit.Panel2MinSize = 0;
                resultSplit.SplitterDistance = Math.Clamp(
                    resultSplit.SplitterDistance,
                    PreviewMinimumWidth,
                    maximum);
                resultSplit.Panel1MinSize = PreviewMinimumWidth;
                resultSplit.Panel2MinSize = ResultMinimumWidth;
            }
            finally
            {
                settingSplitter = false;
            }
        }

        private void BeginSplitterDrag()
        {
            if (splitterDragging)
                return;
            splitterDragging = true;
            FreezeGridColumns(resultGrid);
            FreezeGridColumns(excludedGrid);
        }

        private void EndSplitterDrag()
        {
            if (!splitterDragging)
                return;
            splitterDragging = false;
            RestoreGridColumnFill(resultGrid);
            RestoreGridColumnFill(excludedGrid);
        }

        private static void FreezeGridColumns(DataGridView grid)
        {
            grid.SuspendLayout();
            try
            {
                grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            }
            finally
            {
                grid.ResumeLayout(false);
            }
        }

        private static void RestoreGridColumnFill(DataGridView grid)
        {
            grid.SuspendLayout();
            try
            {
                grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            }
            finally
            {
                grid.ResumeLayout(true);
            }
        }

        private void ApplyCellProtection()
        {
            bool reviewMode = SelectedChoice<CharacterTagAuditExecutionMode>(comboMode) == CharacterTagAuditExecutionMode.Review;
            foreach (DataGridViewRow row in resultGrid.Rows)
            {
                if (row.DataBoundItem is not ReviewRow item)
                    continue;
                DataGridViewCell decision = row.Cells["Decision"];
                DataGridViewCell replacement = row.Cells["Replacement"];
                DataGridViewCell prompt = row.Cells["IncludePrompt"];
                bool editable = reviewMode && item.CanModify;
                decision.ReadOnly = !editable;
                replacement.ReadOnly = !editable || item.Decision != CharacterTagDecision.Replace;
                prompt.ReadOnly = !editable || item.Decision == CharacterTagDecision.Delete;
                // A locked cell must not look like an active dropdown: hide the
                // arrow so "not clickable" is visible before the user clicks.
                if (decision is DataGridViewComboBoxCell comboCell)
                {
                    comboCell.DisplayStyle = editable
                        ? DataGridViewComboBoxDisplayStyle.DropDownButton
                        : DataGridViewComboBoxDisplayStyle.Nothing;
                }
                if (!item.CanModify)
                {
                    decision.Style.BackColor = SystemColors.Control;
                    replacement.Style.BackColor = SystemColors.Control;
                }
                else
                {
                    decision.Style.BackColor = Color.Empty;
                    replacement.Style.BackColor = Color.Empty;
                }
            }
        }

        private static Color DecisionRowColor(CharacterTagDecision decision)
        {
            return decision switch
            {
                CharacterTagDecision.Delete => Color.FromArgb(255, 226, 222),
                CharacterTagDecision.Replace => Color.FromArgb(255, 240, 212),
                CharacterTagDecision.Uncertain => Color.FromArgb(255, 250, 205),
                _ => Color.Empty
            };
        }

        private static void ApplyDecisionRowColors(DataGridView grid)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.DataBoundItem is ReviewRow item)
                    row.DefaultCellStyle.BackColor = DecisionRowColor(item.Decision);
            }
        }

        private void ResultGrid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && resultGrid.Rows[e.RowIndex].DataBoundItem is ReviewRow item)
            {
                if (item.Decision == CharacterTagDecision.Delete)
                    item.IncludeInPrompt = false;
                if (item.Decision != CharacterTagDecision.Replace)
                    item.ReplacementTag = string.Empty;
            }
            ApplyDecisionRowColors(resultGrid);
            ApplyCellProtection();
            RebuildSummaryAndPrompt();
        }

        private void ResultGrid_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            if (resultGrid.CurrentCell?.OwningColumn?.Name != "Decision")
                return;
            if (e.Control is not ComboBox combo)
                return;
            if (activeDecisionCombo != null)
                activeDecisionCombo.SelectedIndexChanged -= ActiveDecisionCombo_SelectedIndexChanged;
            activeDecisionCombo = combo;
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.SelectedIndexChanged += ActiveDecisionCombo_SelectedIndexChanged;
        }

        private void ActiveDecisionCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (resultGrid.IsCurrentCellDirty)
                resultGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void ResultGrid_OpenDecisionDropdown(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;
            if (resultGrid.Columns[e.ColumnIndex].Name != "Decision")
                return;
            DataGridViewCell cell = resultGrid.Rows[e.RowIndex].Cells[e.ColumnIndex];
            if (cell.ReadOnly || resultGrid.ReadOnly)
                return;
            resultGrid.CurrentCell = cell;
            resultGrid.BeginEdit(true);
            // Open the list even when the cell was already in edit mode, so a
            // single click always drops the list down.
            if (resultGrid.EditingControl is ComboBox combo)
                combo.DroppedDown = true;
        }

        private void RefreshDecisionChoices()
        {
            decisionChoices = new[]
            {
                new DecisionChoice(CharacterTagDecision.Keep, I18n.GetText("CharacterTagDecisionKeep")),
                new DecisionChoice(CharacterTagDecision.Delete, I18n.GetText("CharacterTagDecisionDelete")),
                new DecisionChoice(CharacterTagDecision.Replace, I18n.GetText("CharacterTagDecisionReplace")),
                new DecisionChoice(CharacterTagDecision.Uncertain, I18n.GetText("CharacterTagDecisionUncertain"))
            };
            var decisionColumn = (DataGridViewComboBoxColumn)resultGrid.Columns["Decision"];
            decisionColumn.DataSource = decisionChoices;
            decisionColumn.DisplayMember = nameof(DecisionChoice.Text);
            decisionColumn.ValueMember = nameof(DecisionChoice.Value);
        }

        private void ApplyLanguage()
        {
            Text = I18n.GetText("CharacterTagAuditTitle");
            pages.TabPages[0].Text = I18n.GetText("CharacterTagAuditStepSelect");
            pages.TabPages[1].Text = I18n.GetText("CharacterTagAuditStepProgress");
            pages.TabPages[2].Text = I18n.GetText("CharacterTagAuditStepReview");
            FindControl<Label>(pages.TabPages[0], "labelTrigger").Text = I18n.GetText("CharacterTagAuditTrigger");
            FindControl<Label>(pages.TabPages[0], "labelAuditModel").Text = I18n.GetText("CharacterTagAuditModel");
            FindControl<Label>(pages.TabPages[0], "labelAuditStyle").Text = I18n.GetText("CharacterTagAuditStyle");
            FindControl<Label>(pages.TabPages[0], "labelAuditMode").Text = I18n.GetText("CharacterTagAuditMode");
            FindControl<Label>(pages.TabPages[0], "labelMinimumCount").Text = I18n.GetText("CharacterTagAuditMinimumCount");
            FindControl<Label>(pages.TabPages[0], "labelSubjectMode").Text = I18n.GetText("CharacterTagAuditSubjectMode");
            FindControl<Label>(pages.TabPages[0], "labelTriggerB").Text = I18n.GetText("CharacterTagAuditTriggerB");
            FindControl<Label>(pages.TabPages[0], "labelGenders").Text = I18n.GetText("CharacterTagAuditGenders");
            FindControl<Label>(pages.TabPages[0], "labelFolderA").Text = I18n.GetText("CharacterTagAuditFolderA");
            FindControl<Label>(pages.TabPages[0], "labelFolderB").Text = I18n.GetText("CharacterTagAuditFolderB");
            FindControl<Label>(pages.TabPages[0], "labelReferences").Text = I18n.GetText("CharacterTagAuditReferences");
            buttonSetReferenceA.Text = I18n.GetText("CharacterTagAuditSetReferenceA");
            buttonSetReferenceB.Text = I18n.GetText("CharacterTagAuditSetReferenceB");
            UpdateReferenceLabels();
            comboStyle.Items.Clear();
            comboStyle.Items.Add(new LocalizedChoice<CharacterTagAuditStyle>(CharacterTagAuditStyle.Sparse, I18n.GetText("CharacterTagAuditStyleSparse")));
            comboStyle.Items.Add(new LocalizedChoice<CharacterTagAuditStyle>(CharacterTagAuditStyle.Full, I18n.GetText("CharacterTagAuditStyleFull")));
            comboMode.Items.Clear();
            comboMode.Items.Add(new LocalizedChoice<CharacterTagAuditExecutionMode>(CharacterTagAuditExecutionMode.Review, I18n.GetText("CharacterTagAuditModeReview")));
            comboMode.Items.Add(new LocalizedChoice<CharacterTagAuditExecutionMode>(CharacterTagAuditExecutionMode.SummaryApply, I18n.GetText("CharacterTagAuditModeSummary")));
            comboSubjectMode.Items.Clear();
            comboSubjectMode.Items.Add(new LocalizedChoice<CharacterTagAuditSubjectMode>(CharacterTagAuditSubjectMode.Single, I18n.GetText("CharacterTagAuditSubjectSingle")));
            comboSubjectMode.Items.Add(new LocalizedChoice<CharacterTagAuditSubjectMode>(CharacterTagAuditSubjectMode.Dual, I18n.GetText("CharacterTagAuditSubjectDual")));
            comboGenderA.Items.Clear();
            comboGenderA.Items.Add(new LocalizedChoice<CharacterGender>(CharacterGender.Girl, I18n.GetText("CharacterGenderGirl")));
            comboGenderA.Items.Add(new LocalizedChoice<CharacterGender>(CharacterGender.Boy, I18n.GetText("CharacterGenderBoy")));
            comboGenderB.Items.Clear();
            comboGenderB.Items.Add(new LocalizedChoice<CharacterGender>(CharacterGender.Girl, I18n.GetText("CharacterGenderGirl")));
            comboGenderB.Items.Add(new LocalizedChoice<CharacterGender>(CharacterGender.Boy, I18n.GetText("CharacterGenderBoy")));
            comboInitialFilter.Items.Clear();
            comboInitialFilter.Items.Add(new LocalizedChoice<InitialFilter>(InitialFilter.All, I18n.GetText("CharacterTagAuditInitialAll")));
            comboInitialFilter.Items.Add(new LocalizedChoice<InitialFilter>(InitialFilter.Keep, I18n.GetText("CharacterTagAuditInitialKeep")));
            comboInitialFilter.Items.Add(new LocalizedChoice<InitialFilter>(InitialFilter.Changes, I18n.GetText("CharacterTagAuditInitialDelete")));
            comboInitialFilter.SelectedIndex = 0;
            UpdateChoiceDropDownWidth(comboStyle);
            UpdateChoiceDropDownWidth(comboMode);
            textImageSearch.PlaceholderText = I18n.GetText("CharacterTagAuditImageSearch");
            textTagSearch.PlaceholderText = I18n.GetText("CharacterTagAuditTagSearch");
            checkDeletesOnly.Text = I18n.GetText("CharacterTagAuditDeletesOnly");
            buttonRedoVisual.Text = I18n.GetText("CharacterTagAuditRedoVisual");
            buttonBack.Text = I18n.GetText("BtnBack");
            buttonNext.Text = I18n.GetText("BtnNext");
            buttonCancel.Text = I18n.GetText("BtnCancel");
            buttonCopyPrompt.Text = I18n.GetText("CharacterTagAuditCopyPrompt");
            RefreshDecisionChoices();
            resultGrid.Columns["Decision"].HeaderText = I18n.GetText("CharacterTagAuditDecision");
            resultGrid.Columns["Tag"].HeaderText = I18n.GetText("CharacterTagAuditTag");
            resultGrid.Columns["Count"].HeaderText = I18n.GetText("GridCount");
            resultGrid.Columns["Initial"].HeaderText = I18n.GetText("CharacterTagAuditInitial");
            resultGrid.Columns["Replacement"].HeaderText = I18n.GetText("CharacterTagAuditReplacement");
            resultGrid.Columns["IncludePrompt"].HeaderText = I18n.GetText("CharacterTagAuditIncludePrompt");
            resultGrid.Columns["Category"].HeaderText = I18n.GetText("CharacterTagAuditCategory");
            resultGrid.Columns["Reason"].HeaderText = I18n.GetText("CharacterTagAuditReason");
            ApplyAuditGridHeaders(initialGrid);
            excludedGrid.Columns["Tag"].HeaderText = I18n.GetText("CharacterTagAuditTag");
            excludedGrid.Columns["Count"].HeaderText = I18n.GetText("GridCount");
        }

        private static T FindControl<T>(Control root, string name) where T : Control
        {
            return root.Controls.Find(name, true).OfType<T>().First();
        }

        private void LoadDefaults()
        {
            SelectChoice(comboStyle, Program.Settings.CharacterTagAuditStyle);
            SelectChoice(comboMode, Program.Settings.CharacterTagAuditExecutionMode);
            SelectChoice(comboSubjectMode, Program.Settings.CharacterTagAuditSubjectMode);
            SelectChoice(comboGenderA, Program.Settings.CharacterTagAuditGenderA);
            SelectChoice(comboGenderB, Program.Settings.CharacterTagAuditGenderB);
            numericMinimumCount.Value = Math.Clamp(Program.Settings.CharacterTagAuditMinimumCount, 1, (int)numericMinimumCount.Maximum);
            textModel.Text = string.IsNullOrWhiteSpace(Program.Settings.CharacterTagAuditModel)
                ? I18n.GetText("CharacterTagAuditModelRequired")
                : Program.Settings.CharacterTagAuditModel;
            new ToolTip().SetToolTip(textModel, textModel.Text + Environment.NewLine + I18n.GetText("CharacterTagAuditGeminiRecommendation"));
            LoadTriggerCandidates();
            LoadFolderChoices();
            UpdateSubjectModeVisibility();
        }

        private void LoadTriggerCandidates()
        {
            if (Program.DataManager == null)
                return;
            IReadOnlyList<CharacterTagTriggerCandidate> candidates =
                CharacterTagTriggerCandidates.Create(CreateCurrentInventory());
            foreach (ComboBox combo in new[] { comboTrigger, comboTriggerB })
            {
                combo.BeginUpdate();
                combo.Items.Clear();
                foreach (CharacterTagTriggerCandidate candidate in candidates)
                    combo.Items.Add(candidate);
                combo.DisplayMember = nameof(CharacterTagTriggerCandidate.Tag);
                combo.EndUpdate();
            }
        }

        private void LoadFolderChoices()
        {
            if (Program.DataManager == null)
                return;
            IReadOnlyList<DatasetFolderEntry> entries = Program.DataManager.GetFolderEntries();
            foreach (ComboBox combo in new[] { comboFolderA, comboFolderB })
            {
                combo.BeginUpdate();
                combo.Items.Clear();
                combo.Items.Add(new FolderChoice(string.Empty, I18n.GetText("CharacterTagAuditFolderAny")));
                foreach (DatasetFolderEntry entry in entries.Where(entry => entry.RelativePath.Length > 0))
                    combo.Items.Add(new FolderChoice(entry.RelativePath, entry.RelativePath));
                combo.SelectedIndex = 0;
                combo.EndUpdate();
            }
        }

        // Inventory / gallery use the active folder scope of the main window,
        // so a selected repeat folder bounds the whole audit.
        private static CharacterTagInventory CreateCurrentInventory()
        {
            return CharacterTagInventory.Create(
                Program.DataManager.GetScopedItems().Select(item => item.Tags.TextTags.AsEnumerable()));
        }

        private bool IsDualMode => SelectedChoice<CharacterTagAuditSubjectMode>(comboSubjectMode) == CharacterTagAuditSubjectMode.Dual;

        private void UpdateSubjectModeVisibility()
        {
            bool dual = IsDualMode;
            foreach (Control control in dualModeControls)
                control.Visible = dual;
        }

        private void AssignReferenceFromGallery(bool isProfileA)
        {
            if (imageGallery.SelectedItems.Count != 1)
            {
                MessageBox.Show(this, I18n.GetText("CharacterTagAuditImageRequired"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string path = imageGallery.SelectedItems[0].Tag as string;
            if (isProfileA)
                selectedImagePath = path;
            else
                selectedImagePathB = path;
            UpdateReferenceLabels();
        }

        private void UpdateReferenceLabels()
        {
            string none = I18n.GetText("CharacterTagAuditReferenceUnset");
            labelReferenceA.Text = string.IsNullOrEmpty(selectedImagePath)
                ? none
                : Path.GetFileNameWithoutExtension(selectedImagePath);
            labelReferenceB.Text = string.IsNullOrEmpty(selectedImagePathB)
                ? none
                : Path.GetFileNameWithoutExtension(selectedImagePathB);
            // The labels are width-capped with ellipsis; hover shows the rest.
            referenceToolTip.SetToolTip(labelReferenceA, labelReferenceA.Text);
            referenceToolTip.SetToolTip(labelReferenceB, labelReferenceB.Text);
        }

        private readonly ToolTip referenceToolTip = new ToolTip();

        private static string SelectedFolderScope(ComboBox combo)
        {
            return combo.SelectedItem is FolderChoice choice ? choice.RelativePath : string.Empty;
        }

        private List<CharacterAuditProfile> BuildProfiles()
        {
            return new List<CharacterAuditProfile>
            {
                new CharacterAuditProfile
                {
                    TriggerWord = comboTrigger.Text.Trim(),
                    ReferenceImagePath = selectedImagePath,
                    Gender = SelectedChoice<CharacterGender>(comboGenderA),
                    FolderScope = SelectedFolderScope(comboFolderA)
                },
                new CharacterAuditProfile
                {
                    TriggerWord = comboTriggerB.Text.Trim(),
                    ReferenceImagePath = selectedImagePathB,
                    Gender = SelectedChoice<CharacterGender>(comboGenderB),
                    FolderScope = SelectedFolderScope(comboFolderB)
                }
            };
        }

        private string ActiveTrigger()
        {
            return dualRunActive && runProfiles != null && activeResultProfile < runProfiles.Count
                ? runProfiles[activeResultProfile].TriggerWord
                : comboTrigger.Text.Trim();
        }

        private CharacterGender ActiveGender()
        {
            return dualRunActive && runProfiles != null && activeResultProfile >= 0
                && activeResultProfile < runProfiles.Count
                ? runProfiles[activeResultProfile].Gender
                : SelectedChoice<CharacterGender>(comboGenderA);
        }

        private sealed class FolderChoice
        {
            public FolderChoice(string relativePath, string text)
            {
                RelativePath = relativePath;
                Text = text;
            }

            public string RelativePath { get; }
            public string Text { get; }
            public override string ToString() => Text;
        }

        private static void SelectChoice<T>(ComboBox comboBox, T value) where T : struct, Enum
        {
            comboBox.SelectedItem = comboBox.Items.Cast<LocalizedChoice<T>>()
                .First(item => EqualityComparer<T>.Default.Equals(item.Value, value));
        }

        private static T SelectedChoice<T>(ComboBox comboBox) where T : struct, Enum
        {
            if (comboBox.SelectedItem is LocalizedChoice<T> selected)
                return selected.Value;
            LocalizedChoice<T> fallback = comboBox.Items.OfType<LocalizedChoice<T>>().FirstOrDefault();
            if (fallback == null)
                throw new InvalidOperationException("The choice list has not been initialized.");
            return fallback.Value;
        }

        private static void UpdateChoiceDropDownWidth(ComboBox comboBox)
        {
            IEnumerable<int> widths = comboBox.Items.Cast<object>()
                .Select(item => TextRenderer.MeasureText(item?.ToString() ?? string.Empty, comboBox.Font).Width);
            comboBox.DropDownWidth = CharacterTagChoiceLayout.CalculateDropDownWidth(comboBox.ClientSize.Width, widths);
        }

        // Scope fingerprint of the last gallery load (folder + image set).
        private string galleryScopeSignature;

        private string ComputeGalleryScopeSignature()
        {
            if (Program.DataManager == null)
                return string.Empty;
            long hash = 17;
            int count = 0;
            foreach (DataItem item in Program.DataManager.GetScopedItems()
                .OrderBy(item => item.ImageFilePath, StringComparer.OrdinalIgnoreCase))
            {
                hash = unchecked(hash * 31 ^ item.ImageFilePath.GetHashCode());
                count++;
            }
            return (Program.DataManager.ActiveFolder ?? string.Empty) + "|" + count + "|" + hash;
        }

        private async Task ReloadGalleryIfScopeChangedAsync()
        {
            if (IsDisposed || Program.DataManager == null || pages.SelectedIndex != 0)
                return;
            if (string.Equals(ComputeGalleryScopeSignature(), galleryScopeSignature, StringComparison.Ordinal))
                return;
            if (loadingGallery)
            {
                // A load of the OLD scope is still appending thumbnails (33
                // images take a while): cancel it and wait for the loop to
                // unwind instead of silently keeping the stale gallery.
                galleryLoadCancellation?.Cancel();
                for (int i = 0; i < 100 && loadingGallery && !IsDisposed; i++)
                    await Task.Delay(50);
                if (loadingGallery || IsDisposed)
                    return;
                // Another reload may have finished while waiting.
                if (string.Equals(ComputeGalleryScopeSignature(), galleryScopeSignature, StringComparison.Ordinal))
                    return;
            }
            imageGallery.Items.Clear();
            galleryImages.Images.Clear();
            await LoadGalleryAsync();
        }

        private async Task LoadGalleryAsync()
        {
            if (loadingGallery || Program.DataManager == null || IsDisposed)
                return;

            galleryLoadCancellation?.Cancel();
            galleryLoadCancellation?.Dispose();
            galleryLoadCancellation = new CancellationTokenSource();
            CancellationToken token = galleryLoadCancellation.Token;

            galleryScopeSignature = ComputeGalleryScopeSignature();
            loadingGallery = true;
            SafeGalleryBeginUpdate();
            try
            {
                foreach (DataItem item in Program.DataManager.GetScopedItems()
                    .Where(item => Extensions.ImageExtensions.Contains(Path.GetExtension(item.ImageFilePath).ToLowerInvariant()))
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if (IsDisposed || token.IsCancellationRequested)
                        return;
                    string path = item.ImageFilePath;
                    Image thumbnail = await Task.Run(() => ImageLoader.MakeThumb(path, 128), token).ConfigureAwait(true);
                    if (IsDisposed || token.IsCancellationRequested || thumbnail == null)
                        continue;
                    galleryImages.Images.Add(path, thumbnail);
                    imageGallery.Items.Add(new ListViewItem(item.Name, path) { Tag = path });
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                SafeGalleryEndUpdate();
                loadingGallery = false;
            }
        }

        private void SafeGalleryBeginUpdate()
        {
            if (!IsDisposed && imageGallery.IsHandleCreated)
                imageGallery.BeginUpdate();
        }

        private void SafeGalleryEndUpdate()
        {
            if (!IsDisposed && imageGallery.IsHandleCreated)
                imageGallery.EndUpdate();
        }

        private void ResetAuditSession()
        {
            initialAuditItems.Clear();
            initialRows.Clear();
            reviewRows.Clear();
            auditResult = null;
            dualResults = null;
            dualReviewRows = null;
            dualAuditInfo = null;
            runProfiles = null;
            activeResultProfile = -1;
            dualRunActive = false;
            comboResultProfile.Items.Clear();
            comboResultProfile.Visible = false;
            initialGrid.DataSource = null;
            resultGrid.DataSource = null;
            excludedGrid.DataSource = null;
            labelInitialSummary.Text = string.Empty;
            labelSummary.Text = string.Empty;
            textFinalPrompt.Clear();
            labelMetrics.Text = string.Empty;
            labelProgress.Text = string.Empty;
        }

        private void FilterGallery()
        {
            if (Program.DataManager == null || loadingGallery || IsDisposed)
                return;
            string search = textImageSearch.Text.Trim();
            imageGallery.BeginUpdate();
            imageGallery.Items.Clear();
            foreach (DataItem item in Program.DataManager.GetScopedItems()
                .Where(item => galleryImages.Images.ContainsKey(item.ImageFilePath))
                .Where(item => string.IsNullOrEmpty(search) || item.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                imageGallery.Items.Add(new ListViewItem(item.Name, item.ImageFilePath) { Tag = item.ImageFilePath });
            }
            imageGallery.EndUpdate();
        }

        private async Task NextAsync()
        {
            if (pages.SelectedIndex == 0)
            {
                if (!ValidateSelection())
                    return;
                await RunAuditAsync();
            }
            else if (pages.SelectedIndex == 2)
            {
                await ApplyAsync();
            }
        }

        private bool ValidateSelection()
        {
            if (Program.DataManager == null || string.IsNullOrWhiteSpace(Program.DataManager.DatasetRoot))
            {
                MessageBox.Show(this, I18n.GetText("TipDatasetNoLoad"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (string.IsNullOrWhiteSpace(Program.Settings.CharacterTagAuditModel) || Program.OpenAiAutoTagger == null)
            {
                MessageBox.Show(this, I18n.GetText("CharacterTagAuditInvalidSettings"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (string.IsNullOrWhiteSpace(comboTrigger.Text))
            {
                MessageBox.Show(this, I18n.GetText("CharacterTagAuditTriggerRequired"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (!IsDualMode)
            {
                if (imageGallery.SelectedItems.Count != 1)
                {
                    MessageBox.Show(this, I18n.GetText("CharacterTagAuditImageRequired"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                selectedImagePath = imageGallery.SelectedItems[0].Tag as string;
                return true;
            }
            if (string.IsNullOrWhiteSpace(comboTriggerB.Text))
            {
                MessageBox.Show(this, I18n.GetText("CharacterTagAuditTriggerBRequired"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (string.Equals(comboTrigger.Text.Trim(), comboTriggerB.Text.Trim(), StringComparison.Ordinal))
            {
                MessageBox.Show(this, I18n.GetText("CharacterTagAuditTriggersMustDiffer"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (string.IsNullOrEmpty(selectedImagePath) || string.IsNullOrEmpty(selectedImagePathB))
            {
                MessageBox.Show(this, I18n.GetText("CharacterTagAuditReferencesRequired"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        private async Task RunAuditAsync()
        {
            bool dual = IsDualMode;
            ResetAuditSession();
            dualRunActive = dual;
            ShowPage(1);
            buttonNext.Enabled = false;
            ResetAuditProgress(dual ? 4 : 2);
            cancellation = new CancellationTokenSource();
            try
            {
                if (!Program.OpenAiAutoTagger.IsConnected)
                {
                    labelProgress.Text = I18n.GetText("CharacterTagAuditConnecting");
                    var connection = await Program.OpenAiAutoTagger.ConnectAsync(cancellation.Token);
                    if (!connection.Result)
                        throw new InvalidOperationException(connection.ErrMessage);
                }

                CharacterTagAuditService service = CreateAuditService();
                var progress = new Progress<CharacterTagAuditProgress>(UpdateAuditProgress);
                if (dual)
                {
                    runProfiles = BuildProfiles();
                    var dualService = new CharacterTagDualAuditService(service);
                    dualAuditInfo = await dualService.ExecuteAsync(BuildDualAuditOptions(), progress, cancellation.Token);
                    dualResults = dualAuditInfo.ProfileResults.ToList();
                }
                else
                {
                    CharacterTagAuditOptions auditOptions = BuildAuditOptions();
                    auditResult = await service.ExecuteAsync(auditOptions, progress, cancellation.Token);
                }

                Program.Settings.CharacterTagAuditStyle = SelectedChoice<CharacterTagAuditStyle>(comboStyle);
                Program.Settings.CharacterTagAuditExecutionMode = SelectedChoice<CharacterTagAuditExecutionMode>(comboMode);
                Program.Settings.CharacterTagAuditSubjectMode = SelectedChoice<CharacterTagAuditSubjectMode>(comboSubjectMode);
                Program.Settings.CharacterTagAuditGenderA = SelectedChoice<CharacterGender>(comboGenderA);
                Program.Settings.CharacterTagAuditGenderB = SelectedChoice<CharacterGender>(comboGenderB);
                Program.Settings.CharacterTagAuditMinimumCount = (int)numericMinimumCount.Value;
                Program.Settings.SaveSettings();
                if (dual)
                    PrepareDualResults();
                else
                    PrepareResults();
                ShowPage(2);
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed && !closeAfterWork)
                {
                    MessageBox.Show(this, I18n.GetText("CharacterTagAuditCanceled"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    ResetAuditSession();
                    wizardPhase = 0;
                    ShowPage(0);
                }
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !closeAfterWork)
                {
                    MessageBox.Show(
                        this,
                        CharacterTagAuditErrorFormatter.Format(ex, I18n.GetText),
                        Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    ShowPage(0);
                }
            }
            finally
            {
                cancellation?.Dispose();
                cancellation = null;
                if (!IsDisposed)
                    buttonNext.Enabled = true;
                if (closeAfterWork)
                {
                    closeAfterWork = false;
                    if (!IsDisposed)
                        Close();
                }
            }
        }

        private CharacterTagAuditService CreateAuditService()
        {
            return new CharacterTagAuditService(async (request, token) =>
            {
                var openAiRequest = new OpenAiRequest
                {
                    Model = request.Model,
                    SystemPrompt = request.SystemPrompt,
                    UserPrompt = request.UserPrompt
                };
                openAiRequest.ImagePath.AddRange(request.ImagePaths);
                OpenAiDetailedResponse response = await Program.OpenAiAutoTagger.SendDetailedRequestAsync(openAiRequest, token);
                CharacterTagTokenUsage usage = response.TotalTokens.HasValue
                    ? new CharacterTagTokenUsage(response.InputTokens ?? 0, response.OutputTokens ?? 0, response.TotalTokens.Value)
                    : null;
                return new CharacterTagModelResponse(response.Result, response.ErrMessage, usage);
            });
        }

        private CharacterTagAuditOptions BuildAuditOptions()
        {
            CharacterTagSkillBundle skills = CharacterTagSkillLoader.Load(AppContext.BaseDirectory);
            return new CharacterTagAuditOptions
            {
                Inventory = CreateCurrentInventory(),
                TriggerWord = comboTrigger.Text.Trim(),
                Style = SelectedChoice<CharacterTagAuditStyle>(comboStyle),
                MinimumCount = (int)numericMinimumCount.Value,
                Model = Program.Settings.CharacterTagAuditModel,
                ReferenceImagePath = selectedImagePath,
                CharacterAuditorSkill = skills.CharacterAuditor,
                PromptPyramidSkill = skills.PromptPyramid
            };
        }

        private CharacterTagDualAuditOptions BuildDualAuditOptions()
        {
            CharacterTagSkillBundle skills = CharacterTagSkillLoader.Load(AppContext.BaseDirectory);
            return new CharacterTagDualAuditOptions
            {
                Images = Program.DataManager.GetScopedItems()
                    .Select(item => new CharacterImageTagRecord
                    {
                        ImagePath = item.ImageFilePath,
                        Tags = item.Tags.TextTags.ToList()
                    })
                    .ToList(),
                DatasetRoot = Program.DataManager.DatasetRoot,
                Profiles = runProfiles,
                Style = SelectedChoice<CharacterTagAuditStyle>(comboStyle),
                MinimumCount = (int)numericMinimumCount.Value,
                Model = Program.Settings.CharacterTagAuditModel,
                CharacterAuditorSkill = skills.CharacterAuditor,
                PromptPyramidSkill = skills.PromptPyramid
            };
        }

        private async Task RedoVisualReviewAsync()
        {
            // Dual runs hide this button; the guard keeps a stray invocation
            // (e.g. hotkey navigation) from mixing profiles.
            if (dualRunActive || initialAuditItems.Count == 0 || cancellation != null)
                return;
            string newImagePath = PickReferenceImage();
            if (string.IsNullOrEmpty(newImagePath))
                return;
            selectedImagePath = newImagePath;
            ShowPage(1);
            ResetAuditProgress(1);
            buttonNext.Enabled = false;
            cancellation = new CancellationTokenSource();
            try
            {
                if (!Program.OpenAiAutoTagger.IsConnected)
                {
                    labelProgress.Text = I18n.GetText("CharacterTagAuditConnecting");
                    var connection = await Program.OpenAiAutoTagger.ConnectAsync(cancellation.Token);
                    if (!connection.Result)
                        throw new InvalidOperationException(connection.ErrMessage);
                }
                CharacterTagAuditService service = CreateAuditService();
                var progress = new Progress<CharacterTagAuditProgress>(UpdateAuditProgress);
                auditResult = await service.ExecuteVisualReviewAsync(
                    BuildAuditOptions(), initialAuditItems, progress, cancellation.Token);
                PrepareResults();
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed && !closeAfterWork)
                    MessageBox.Show(this, I18n.GetText("CharacterTagAuditCanceled"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !closeAfterWork)
                {
                    MessageBox.Show(
                        this,
                        CharacterTagAuditErrorFormatter.Format(ex, I18n.GetText),
                        Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            finally
            {
                cancellation?.Dispose();
                cancellation = null;
                if (!IsDisposed)
                {
                    buttonNext.Enabled = true;
                    ShowPage(2);
                }
                if (closeAfterWork)
                {
                    closeAfterWork = false;
                    if (!IsDisposed)
                        Close();
                }
            }
        }

        private string PickReferenceImage()
        {
            using var dialog = new Form
            {
                Text = I18n.GetText("CharacterTagAuditRedoVisualTitle"),
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                ClientSize = new Size(720, 520),
                MinimumSize = new Size(480, 360)
            };
            var picker = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.LargeIcon,
                MultiSelect = false,
                HideSelection = false,
                LargeImageList = galleryImages
            };
            foreach (DataItem item in Program.DataManager.GetScopedItems()
                .Where(item => galleryImages.Images.ContainsKey(item.ImageFilePath))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                var entry = new ListViewItem(item.Name, item.ImageFilePath) { Tag = item.ImageFilePath };
                picker.Items.Add(entry);
                if (string.Equals(item.ImageFilePath, selectedImagePath, StringComparison.OrdinalIgnoreCase))
                    entry.Selected = true;
            }
            FlowLayoutPanel dialogActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8)
            };
            var confirm = new Button { Text = I18n.GetText("BtnOK"), AutoSize = true, DialogResult = DialogResult.OK };
            var abort = new Button { Text = I18n.GetText("BtnCancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
            dialogActions.Controls.AddRange(new Control[] { abort, confirm });
            picker.ItemActivate += (_, _) =>
            {
                if (picker.SelectedItems.Count == 1)
                    dialog.DialogResult = DialogResult.OK;
            };
            dialog.Controls.Add(picker);
            dialog.Controls.Add(dialogActions);
            dialog.AcceptButton = confirm;
            dialog.CancelButton = abort;
            if (dialog.ShowDialog(this) != DialogResult.OK || picker.SelectedItems.Count != 1)
                return null;
            return picker.SelectedItems[0].Tag as string;
        }

        private void PrepareResults()
        {
            string trigger = comboTrigger.Text.Trim();
            reviewRows = auditResult.Items.Select(item => CreateReviewRow(item, trigger, includeInitial: true)).ToList();
            referencePreview.Image?.Dispose();
            referencePreview.Image = ImageLoader.LoadPreview(selectedImagePath, PreviewMaximumDimension);
            ApplyReviewModeToResultControls();
            comboResultProfile.Visible = false;
            buttonRedoVisual.Visible = true;
            buttonRedoVisual.Enabled = initialAuditItems.Count > 0;
            excludedGrid.DataSource = auditResult.ExcludedItems.ToList();
            CollapseExcludedGrid();
            FillResultGrid();
            _ = LocalizeReasonsAsync(reviewRows, resultGrid);
            UpdateMetrics();
            splitterAdjustedByUser = false;
            SchedulePreviewWidthUpdate();
        }

        private void PrepareDualResults()
        {
            dualReviewRows = new List<List<ReviewRow>>();
            for (int i = 0; i < dualResults.Count; i++)
            {
                string trigger = runProfiles[i].TriggerWord;
                dualReviewRows.Add(dualResults[i].Items
                    .Select(item => CreateReviewRow(item, trigger, includeInitial: true))
                    .ToList());
            }
            ApplyReviewModeToResultControls();
            buttonRedoVisual.Visible = false;
            comboResultProfile.Items.Clear();
            for (int i = 0; i < runProfiles.Count; i++)
            {
                comboResultProfile.Items.Add(string.Format(
                    I18n.GetText("CharacterTagAuditProfileTab"),
                    (char)('A' + i),
                    runProfiles[i].TriggerWord));
            }
            comboResultProfile.Visible = true;
            activeResultProfile = -1;
            comboResultProfile.SelectedIndex = 0;
            foreach (List<ReviewRow> rows in dualReviewRows)
                _ = LocalizeReasonsAsync(rows, resultGrid);
            splitterAdjustedByUser = false;
        }

        private void SwitchResultProfile(int index)
        {
            if (!dualRunActive || dualReviewRows == null
                || index < 0 || index >= dualReviewRows.Count || index == activeResultProfile)
            {
                return;
            }
            activeResultProfile = index;
            auditResult = dualResults[index];
            reviewRows = dualReviewRows[index];
            referencePreview.Image?.Dispose();
            referencePreview.Image = ImageLoader.LoadPreview(
                runProfiles[index].ReferenceImagePath, PreviewMaximumDimension);
            excludedGrid.DataSource = auditResult.ExcludedItems.ToList();
            CollapseExcludedGrid();
            FillResultGrid();
            UpdateMetrics();
            SchedulePreviewWidthUpdate();
        }

        private void ApplyReviewModeToResultControls()
        {
            bool reviewMode = SelectedChoice<CharacterTagAuditExecutionMode>(comboMode) == CharacterTagAuditExecutionMode.Review;
            resultGrid.ReadOnly = !reviewMode;
            textTagSearch.Enabled = reviewMode;
            checkDeletesOnly.Checked = !reviewMode;
            checkDeletesOnly.Enabled = reviewMode;
        }

        private void CollapseExcludedGrid()
        {
            excludedExpanded = false;
            excludedGrid.Visible = false;
            ((TableLayoutPanel)excludedGrid.Parent).RowStyles[6].Height = 0;
            UpdateExcludedButtonText();
        }

        private void SchedulePreviewWidthUpdate()
        {
            if (!IsDisposed)
            {
                BeginInvoke(() =>
                {
                    if (!IsDisposed)
                        UpdatePreviewWidth();
                });
            }
        }

        private void ShowInitialResults(IReadOnlyList<CharacterTagAuditItem> items)
        {
            if (IsDisposed)
                return;
            initialAuditItems = items.ToList();
            initialRows = items.Select(item => CreateReviewRow(item, string.Empty, includeInitial: false)).ToList();
            int keep = initialRows.Count(row => row.Decision != CharacterTagDecision.Delete && row.Decision != CharacterTagDecision.Replace);
            int delete = initialRows.Count(row => row.Decision == CharacterTagDecision.Delete);
            int replace = initialRows.Count(row => row.Decision == CharacterTagDecision.Replace);
            labelInitialSummary.Text = string.Format(I18n.GetText("CharacterTagAuditInitialSummaryV2"), keep, delete, replace);
            FillInitialGrid();
            _ = LocalizeReasonsAsync(initialRows, initialGrid);
        }

        private void FillInitialGrid()
        {
            if (comboInitialFilter.SelectedItem is not LocalizedChoice<InitialFilter> selection)
                return;
            // Default view: every audited tag, change candidates sorted to the
            // top so mixed decisions are visible without touching the filter.
            IEnumerable<ReviewRow> rows = selection.Value switch
            {
                InitialFilter.Keep => initialRows.Where(row => !IsChangeCandidate(row)),
                InitialFilter.Changes => initialRows.Where(IsChangeCandidate),
                _ => initialRows.OrderBy(DecisionSortRank)
            };
            initialGrid.DataSource = new BindingList<ReviewRow>(rows.ToList());
        }

        private static bool IsChangeCandidate(ReviewRow row)
        {
            return row.Decision == CharacterTagDecision.Delete
                || row.Decision == CharacterTagDecision.Replace
                || row.Decision == CharacterTagDecision.Uncertain;
        }

        private static int DecisionSortRank(ReviewRow row)
        {
            return row.Decision switch
            {
                CharacterTagDecision.Delete => 0,
                CharacterTagDecision.Replace => 1,
                CharacterTagDecision.Uncertain => 2,
                _ => 3
            };
        }

        private void FillResultGrid()
        {
            IEnumerable<ReviewRow> rows = reviewRows;
            string search = textTagSearch.Text.Trim();
            if (!string.IsNullOrEmpty(search))
                rows = rows.Where(row => row.Tag.Contains(search, StringComparison.OrdinalIgnoreCase));
            if (checkDeletesOnly.Checked)
                rows = rows.Where(row => row.Decision == CharacterTagDecision.Delete || row.Decision == CharacterTagDecision.Replace);
            resultGrid.DataSource = new BindingList<ReviewRow>(rows.ToList());
            RebuildSummaryAndPrompt();
        }

        private void RebuildSummaryAndPrompt()
        {
            if (auditResult == null)
                return;
            int deleteCount = reviewRows.Count(row => row.Decision == CharacterTagDecision.Delete && row.CanModify);
            int replaceCount = reviewRows.Count(row => row.Decision == CharacterTagDecision.Replace && row.CanModify);
            int affected = dualRunActive
                ? CountAffectedFilesDual()
                : CountAffectedFiles(reviewRows
                    .Where(row => row.CanModify && (row.Decision == CharacterTagDecision.Delete || row.Decision == CharacterTagDecision.Replace))
                    .Select(row => row.Tag));
            int keepCount = reviewRows.Count - deleteCount - replaceCount;
            labelSummary.Text = string.Format(
                I18n.GetText("CharacterTagAuditDetailedSummaryV2"),
                SelectedChoice<CharacterTagAuditStyle>(comboStyle) == CharacterTagAuditStyle.Sparse
                    ? I18n.GetText("CharacterTagAuditStyleSparse")
                    : I18n.GetText("CharacterTagAuditStyleFull"),
                reviewRows.Count,
                keepCount,
                deleteCount,
                replaceCount,
                auditResult.ExcludedItems.Count,
                affected);
            if (dualRunActive && dualAuditInfo != null)
            {
                labelSummary.Text += Environment.NewLine + string.Format(
                    I18n.GetText("CharacterTagAuditDualSummary"),
                    dualAuditInfo.MemberImageCounts[0],
                    dualAuditInfo.MemberImageCounts[1],
                    dualAuditInfo.SharedImageCount,
                    dualAuditInfo.UnattributedImageCount);
            }
            // A per-character prompt describes that character alone: shared
            // images may have injected 2girls/multiple girls — swap for the
            // active character's own subject tag.
            textFinalPrompt.Text = CharacterPromptSubjectNormalizer.NormalizeToSingle(
                CharacterTagPromptBuilder.Build(
                    BuildCanonicalizedAuditItems(),
                    ActiveTrigger()),
                ActiveGender());
        }

        private List<IReadOnlyDictionary<string, CharacterTagAuditItem>> BuildDualDecisionDictionaries()
        {
            return dualReviewRows
                .Select(rows => (IReadOnlyDictionary<string, CharacterTagAuditItem>)rows
                    .Select(ToAuditItem)
                    .ToDictionary(item => item.Tag, StringComparer.Ordinal))
                .ToList();
        }

        private IReadOnlyList<EditableTag> TransformEditableTagsDualForItem(
            DataItem item,
            IReadOnlyList<IReadOnlyDictionary<string, CharacterTagAuditItem>> decisionsByProfile)
        {
            IReadOnlyList<int> present = CharacterImageMembership.GetPresentProfiles(
                item.Tags.TextTags, item.ImageFilePath, Program.DataManager.DatasetRoot, runProfiles);
            if (present.Count == 0)
                return item.Tags.Cast<EditableTag>().Select(tag => (EditableTag)tag.Clone()).ToList();
            IReadOnlyDictionary<string, CharacterTagAuditItem> effective =
                CharacterTagMultiAuditPlan.BuildEffectiveDecisions(present, decisionsByProfile);
            IReadOnlyList<EditableTag> transformed = TransformEditableTags(item.Tags, effective.Values);
            if (present.Count == 2)
            {
                transformed = CharacterTagEditableTagInjector.ApplySubjectCount(
                    transformed, runProfiles[present[0]], runProfiles[present[1]]);
            }
            return transformed;
        }

        private int CountAffectedFilesDual()
        {
            if (dualReviewRows == null || runProfiles == null)
                return 0;
            List<IReadOnlyDictionary<string, CharacterTagAuditItem>> decisions = BuildDualDecisionDictionaries();
            return Program.DataManager.GetScopedItems().Count(item =>
                !item.Tags.TextTags.SequenceEqual(
                    TransformEditableTagsDualForItem(item, decisions).Select(tag => tag.Tag),
                    StringComparer.Ordinal));
        }

        private List<CharacterTagAuditItem> BuildCanonicalizedAuditItems()
        {
            List<CharacterTagAuditItem> items = reviewRows.Select(ToAuditItem).ToList();
            CharacterTagResultCanonicalizer.Apply(
                items,
                SelectedChoice<CharacterTagAuditStyle>(comboStyle));
            return items;
        }

        private void UpdateMetrics()
        {
            CharacterTagAuditMetrics metrics = auditResult?.Metrics;
            if (metrics == null)
                return;
            string usage = metrics.HasTokenUsage
                ? string.Format(I18n.GetText("CharacterTagAuditTokenUsage"), metrics.InputTokens, metrics.OutputTokens, metrics.TotalTokens)
                : I18n.GetText("CharacterTagAuditTokenUnavailable");
            labelMetrics.Text = string.Format(I18n.GetText("CharacterTagAuditMetrics"), metrics.TotalDuration.TotalSeconds, usage);
            metricsToolTip.SetToolTip(labelMetrics, string.Join(Environment.NewLine, metrics.Requests.Select((request, index) =>
                string.Format(I18n.GetText("CharacterTagAuditMetricDetail"), index + 1, request.Stage, request.Duration.TotalSeconds,
                    request.Usage == null ? I18n.GetText("CharacterTagAuditTokenUnavailable") : request.Usage.TotalTokens.ToString()))));
        }

        private int CountAffectedFiles(IEnumerable<string> tags)
        {
            var set = new HashSet<string>(tags, StringComparer.Ordinal);
            return Program.DataManager.DataSet.Values.Count(item => item.Tags.TextTags.Any(set.Contains));
        }

        private async Task ApplyAsync()
        {
            resultGrid.EndEdit();
            // Dual review flows character by character: 应用并保存 on an
            // earlier profile just advances to the next character's review
            // (after validating the current one); the real apply-and-write
            // runs from the last profile.
            if (dualRunActive && dualReviewRows != null
                && activeResultProfile >= 0 && activeResultProfile < dualReviewRows.Count - 1)
            {
                string currentError = ValidateEditedDecisions(
                    dualReviewRows[activeResultProfile].Select(ToAuditItem).ToList());
                if (!string.IsNullOrEmpty(currentError))
                {
                    MessageBox.Show(this, currentError, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                SwitchResultProfile(activeResultProfile + 1);
                return;
            }
            List<List<CharacterTagAuditItem>> decisionsByProfile;
            if (dualRunActive)
            {
                decisionsByProfile = dualReviewRows
                    .Select(rows => rows.Select(ToAuditItem).ToList())
                    .ToList();
            }
            else
            {
                decisionsByProfile = new List<List<CharacterTagAuditItem>> { reviewRows.Select(ToAuditItem).ToList() };
            }
            foreach (List<CharacterTagAuditItem> decisionsOfProfile in decisionsByProfile)
            {
                string validationError = ValidateEditedDecisions(decisionsOfProfile);
                if (!string.IsNullOrEmpty(validationError))
                {
                    MessageBox.Show(this, validationError, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            buttonNext.Enabled = false;
            buttonBack.Enabled = false;
            applyInProgress = true;
            try
            {
                labelProgress.Text = I18n.GetText("CharacterTagAuditSavePreparing");
                // Synchronous repaint instead of Application.DoEvents(): DoEvents
                // pumps the queue and allows arbitrary reentrancy mid-apply.
                labelProgress.Refresh();

                List<CharacterTagAuditItem> decisions = decisionsByProfile[0];
                List<IReadOnlyDictionary<string, CharacterTagAuditItem>> dualDecisions = dualRunActive
                    ? decisionsByProfile
                        .Select(items => (IReadOnlyDictionary<string, CharacterTagAuditItem>)items
                            .ToDictionary(item => item.Tag, StringComparer.Ordinal))
                        .ToList()
                    : null;
                List<(DataItem Item, IReadOnlyList<EditableTag> Tags)> affected = await Task.Run(() =>
                    Program.DataManager.GetScopedItems()
                        .Select(item => (Item: item, Tags: dualRunActive
                            ? TransformEditableTagsDualForItem(item, dualDecisions)
                            : TransformEditableTags(item.Tags, decisions)))
                        .Where(change => !change.Item.Tags.TextTags.SequenceEqual(change.Tags.Select(tag => tag.Tag), StringComparer.Ordinal))
                        .ToList());

                int changeCount = decisionsByProfile.Sum(items => items.Count(item => item.ShouldDelete || item.ShouldReplace));
                if (affected.Count == 0)
                {
                    MessageBox.Show(this, I18n.GetText("CharacterTagAuditNothingToDelete"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                string confirm = string.Format(I18n.GetText("CharacterTagAuditApplyConfirmV2"), changeCount, affected.Count);
                if (MessageBox.Show(this, confirm, Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                    return;

                string separator = Program.Settings.SeparatorOnSave.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t");
                List<CharacterTagFileChange> changes = affected.Select(change => new CharacterTagFileChange(
                    change.Item.TextFilePath,
                    string.Join(separator, change.Tags.Select(tag => tag.ToString())))).ToList();

                int total = changes.Count;
                var progress = new Progress<int>(completed =>
                {
                    labelProgress.Text = string.Format(I18n.GetText("CharacterTagAuditSaveProgress"), completed, total);
                });
                labelProgress.Text = string.Format(I18n.GetText("CharacterTagAuditSaveProgress"), 0, total);

                // Off the UI thread: CommitAsync performs ~3 disk operations per
                // file (stage/backup/rename); on the UI thread a large dataset
                // froze the window and the progress label never repainted.
                // Progress<T> was created on the UI thread, so callbacks still
                // marshal back correctly.
                await Task.Run(() => CharacterTagFileTransaction.CommitAsync(
                    Program.DataManager.DatasetRoot,
                    changes,
                    progress: progress));

                labelProgress.Text = I18n.GetText("CharacterTagAuditSaveUpdatingMemory");
                Program.DataManager.ExecuteBulkMutation(() =>
                {
                    foreach ((DataItem item, IReadOnlyList<EditableTag> tags) in affected)
                    {
                        item.Tags.Clear();
                        foreach (EditableTag tag in tags)
                            item.Tags.Add((EditableTag)tag.Clone(), true);
                        item.AcceptCurrentTagsAsSaved();
                    }
                });
                Program.DataManager.UpdateDatasetHash();
                owner.RefreshAfterCharacterTagAudit();
                MessageBox.Show(this, I18n.GetText("CharacterTagAuditSaved"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                applyInProgress = false;
                Close();
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !closeAfterWork)
                    MessageBox.Show(this, string.Format(I18n.GetText("CharacterTagAuditSaveFailed"), ex.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                applyInProgress = false;
                if (!IsDisposed)
                {
                    buttonNext.Enabled = true;
                    buttonBack.Enabled = true;
                }
                if (closeAfterWork)
                {
                    closeAfterWork = false;
                    if (!IsDisposed)
                        Close();
                }
            }
        }

        private static IReadOnlyList<EditableTag> TransformEditableTags(
            EditableTagList originalTags,
            IEnumerable<CharacterTagAuditItem> decisions)
        {
            var byTag = decisions.ToDictionary(item => item.Tag, StringComparer.Ordinal);
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<EditableTag>();
            foreach (EditableTag original in originalTags)
            {
                if (byTag.TryGetValue(original.Tag, out CharacterTagAuditItem decision) && decision.ShouldDelete)
                    continue;
                EditableTag transformed = (EditableTag)original.Clone();
                if (byTag.TryGetValue(original.Tag, out decision) && decision.ShouldReplace)
                    transformed.Tag = decision.ReplacementTag;
                if (emitted.Add(transformed.Tag))
                    result.Add(transformed);
            }
            return result;
        }

        private string ValidateEditedDecisions(IReadOnlyList<CharacterTagAuditItem> decisions)
        {
            foreach (CharacterTagAuditItem item in decisions)
            {
                if (item.FinalDecision == CharacterTagDecision.Replace
                    && !CharacterTagAuditPolicy.IsValidReplacement(item.Tag, item.ReplacementTag))
                    return string.Format(I18n.GetText("CharacterTagAuditInvalidReplacement"), item.Tag);
            }
            var replacementSources = new HashSet<string>(decisions
                .Where(item => item.FinalDecision == CharacterTagDecision.Replace)
                .Select(item => item.Tag), StringComparer.Ordinal);
            if (decisions.Any(item => item.FinalDecision == CharacterTagDecision.Replace
                && replacementSources.Contains(item.ReplacementTag)))
                return I18n.GetText("CharacterTagAuditReplacementChain");
            return string.Empty;
        }

        private static CharacterTagAuditItem ToAuditItem(ReviewRow row)
        {
            return new CharacterTagAuditItem
            {
                Tag = row.Tag,
                Count = row.Count,
                InitialDecision = row.InitialValue,
                FinalDecision = row.CanModify ? row.Decision : CharacterTagDecision.Keep,
                Category = row.CategoryValue,
                Reason = row.OriginalReason,
                ReplacementTag = row.ReplacementTag?.Trim() ?? string.Empty,
                IncludeInPrompt = row.Decision != CharacterTagDecision.Delete && row.IncludeInPrompt,
                PromptOrder = row.PromptOrder
            };
        }

        private void ButtonCancel_Click(object sender, EventArgs e)
        {
            if (cancellation != null)
                cancellation.Cancel();
            else
                Close();
        }

        private void ShowPage(int index)
        {
            if (IsDisposed)
                return;
            wizardPhase = Math.Max(wizardPhase, index);
            pages.Selecting -= PreventSelection;
            pages.SelectedIndex = index;
            pages.Selecting += PreventSelection;
            buttonBack.Visible = false;
            buttonNext.Visible = index != 1;
            buttonNext.Text = index == 2 ? I18n.GetText("CharacterTagAuditApply") : I18n.GetText("BtnNext");
            if (index == 0 && wizardPhase == 0)
                ResetAuditProgress(2);
        }

        private void ResetAuditProgress(int totalSteps)
        {
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Minimum = 0;
            progressBar.Maximum = Math.Max(1, totalSteps);
            progressBar.Value = 0;
        }

        private void UpdateAuditProgress(CharacterTagAuditProgress update)
        {
            if (IsDisposed)
                return;
            int totalSteps = Math.Max(1, update.TotalSteps);
            int completedSteps = Math.Clamp(update.CompletedSteps, 0, totalSteps);
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Minimum = 0;
            progressBar.Maximum = totalSteps;
            progressBar.Value = completedSteps;

            string stageText = update.Stage switch
            {
                CharacterTagAuditStage.TextScreening => I18n.GetText("CharacterTagAuditTextScreening"),
                CharacterTagAuditStage.TextScreeningCompleted => I18n.GetText("CharacterTagAuditTextScreening"),
                CharacterTagAuditStage.VisualReview => I18n.GetText("CharacterTagAuditVisualReview"),
                _ => string.Empty
            };

            // The running step is always the one after the completed count
            // (works for both the single 2-step run and the dual 4-step run).
            int displayStep = Math.Min(totalSteps, completedSteps + 1);
            string progressText = string.IsNullOrEmpty(stageText)
                ? string.Empty
                : string.Format(I18n.GetText("CharacterTagAuditProgressStep"), displayStep, totalSteps, stageText);
            if (dualRunActive && runProfiles != null && !string.IsNullOrEmpty(progressText)
                && update.ProfileIndex < runProfiles.Count)
            {
                progressText = string.Format(
                    I18n.GetText("CharacterTagAuditProfileTab"),
                    (char)('A' + update.ProfileIndex),
                    runProfiles[update.ProfileIndex].TriggerWord) + " - " + progressText;
            }
            labelProgress.Text = progressText;

            if (update.Stage == CharacterTagAuditStage.TextScreeningCompleted && update.Items != null)
                ShowInitialResults(update.Items);
        }

        private void PreventSelection(object sender, TabControlCancelEventArgs e)
        {
            if (e.TabPageIndex != pages.SelectedIndex)
                e.Cancel = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                galleryLoadCancellation?.Cancel();
                galleryLoadCancellation?.Dispose();
                galleryLoadCancellation = null;
                cancellation?.Cancel();
                cancellation?.Dispose();
                referencePreview.Image?.Dispose();
                galleryImages.Dispose();
                referenceToolTip.Dispose();
                translationCancellation.Cancel();
                translationCancellation.Dispose();
                reasonTranslator?.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed class ReviewRow
        {
            public CharacterTagDecision Decision { get; set; }
            public bool CanModify { get; set; }
            public string Tag { get; set; }
            public int Count { get; set; }
            public CharacterTagDecision InitialValue { get; set; }
            public string InitialDisplay { get; set; }
            public string FinalDisplay { get; set; }
            public string ReplacementTag { get; set; }
            public bool IncludeInPrompt { get; set; }
            public int PromptOrder { get; set; }
            public CharacterTagCategory CategoryValue { get; set; }
            public string CategoryDisplay { get; set; }
            public string OriginalReason { get; set; }
            public string ReasonDisplay { get; set; }
            public bool ReasonTranslationFailed { get; set; }
        }

        private sealed class DecisionChoice
        {
            public DecisionChoice(CharacterTagDecision value, string text)
            {
                Value = value;
                Text = text;
            }

            public CharacterTagDecision Value { get; }
            public string Text { get; }
        }

        private enum InitialFilter
        {
            All,
            Keep,
            // Delete, Replace, and Uncertain rows — everything needing attention.
            Changes
        }

        private sealed class LocalizedChoice<T> where T : struct, Enum
        {
            public LocalizedChoice(T value, string text)
            {
                Value = value;
                Text = text;
            }

            public T Value { get; }
            public string Text { get; }
            public override string ToString() => Text;
        }
    }
}
