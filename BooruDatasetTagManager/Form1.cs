using BooruDatasetTagManager.AiApi;
using BooruDatasetTagManager.Properties;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static BooruDatasetTagManager.DatasetManager;

namespace BooruDatasetTagManager
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
            previewPicBox = new PictureBox();
            previewPicBox.Name = "previewPicBox";
            allTagsFilter = new Form_filter();
            CreateLangMenuItems();
            InitHotkeyCommands();
            InitializeAiServerSetAndTestMenu();
            //test color scheme
            //Program.ColorManager.SelectScheme("Dark");
            Program.ColorManager.ChangeColorScheme(this, Program.ColorManager.SelectedScheme);
            Program.ColorManager.ChangeColorSchemeInConteiner(Controls, Program.ColorManager.SelectedScheme);
            Program.ColorManager.SchemeChanded += ColorManager_SchemeChanded;
            contextMenuImageGridHeader.ItemClicked += ContextMenuImageGridHeader_ItemClicked;
            gridViewTags.CellFormatting += GridViewTags_CellFormatting;
            InitializeTagContextMenu();
            switchLanguage();
        }

        /// <summary>
        /// Multi-select view stores the tag text only on the first row of each
        /// tag group (the remaining rows exist per image and would otherwise look
        /// like empty rows); show the group tag dimmed on the continuation rows.
        /// </summary>
        private void GridViewTags_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;
            if (gridViewTags.Columns[e.ColumnIndex].Name != "ImageTags")
                return;
            if (gridViewTags.DataSource is not MultiSelectDataTable dt)
                return;
            if (!string.IsNullOrEmpty(e.Value as string))
                return;
            if (e.RowIndex >= dt.Rows.Count)
                return;
            var row = dt.Rows[e.RowIndex] as MultiSelectDataRow;
            if (row == null || row.RowState == System.Data.DataRowState.Deleted || row.RowState == System.Data.DataRowState.Detached)
                return;
            string groupTag = row.GetTagText();
            if (string.IsNullOrEmpty(groupTag))
                return;
            e.Value = groupTag;
            e.CellStyle.ForeColor = Color.Gray;
            e.FormattingApplied = true;
        }

        private ToolStripMenuItem menuAiServerSet;
        private ToolStripMenuItem menuTestModule;
        private ToolStripMenuItem menuLlmTagger;
        private ToolStripMenuItem menuVideoConvert;
        private ToolStripMenuItem menuVideoExtract;
        private ToolStripMenuItem menuOnnxTagger;
        private ToolStripMenuItem menuContextDSVideoTools;
        private ToolStripMenuItem menuContextDSRetagOnnx;
        private ToolStripMenuItem menuContextDSRetagLlm;

        internal List<string> GetSelectedDatasetVideoPaths()
        {
            var paths = new List<string>();
            for (int i = 0; i < gridViewDS.SelectedRows.Count; i++)
            {
                string path = (string)gridViewDS.SelectedRows[i].Cells["ImageFilePath"].Value;
                if (VideoProcessingService.IsVideoFile(path))
                    paths.Add(path);
            }

            return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal List<string> GetSelectedDatasetImagePaths()
        {
            var paths = new List<string>();
            for (int i = 0; i < gridViewDS.SelectedRows.Count; i++)
            {
                string path = (string)gridViewDS.SelectedRows[i].Cells["ImageFilePath"].Value;
                string extension = Path.GetExtension(path).ToLowerInvariant();
                if (Extensions.ImageExtensions.Contains(extension) && !VideoProcessingService.IsVideoFile(path))
                    paths.Add(path);
            }

            return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal void RefreshDatasetGrid(IReadOnlyList<string> addedPaths = null)
        {
            if (Program.DataManager == null)
                return;

            SaveSelectedInViewDs();
            gridViewDS.DataSource = Program.DataManager.GetDataSourceWithLastFilter();
            LoadSelectedInViewDs();
            SetDSCountStatus(string.Format(I18n.GetText("LabelShownDsImages"), gridViewDS.RowCount, Program.DataManager.DataSet.Count));
            if (addedPaths != null && addedPaths.Count > 0)
                SetStatus(string.Format(I18n.GetText("VideoExtractImportedCount"), addedPaths.Count));
        }

        internal void RefreshSelectedImageTags()
        {
            LoadSelectedImageToGrid();
        }

        internal void PrepareForBulkTagWrite()
        {
            LockEdit(true);
            gridViewTags.DataSource = null;
            gridViewTags.Rows.Clear();
        }

        internal void CompleteBulkTagWrite()
        {
            RefreshSelectedImageTagsAfterBulkWrite();
            gridViewAllTags.Refresh();
            LockEdit(false);
        }

        internal void RefreshSelectedImageTagsAfterBulkWrite()
        {
            if (Program.DataManager == null || gridViewDS.SelectedRows.Count == 0)
                return;

            if (gridViewDS.SelectedRows.Count == 1)
            {
                string imagePath = (string)gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value;
                if (!Program.DataManager.DataSet.TryGetValue(imagePath, out DataItem dataItem))
                    return;

                gridViewTags.AutoGenerateColumns = false;
                gridViewTags.SuspendLayout();
                BtnTagImageChecker.Enabled = false;
                gridViewTags.Tag = imagePath;
                ChageImageColumn(false);
                gridViewTags.DataSource = dataItem.Tags;
                gridViewTags.AllowDrop = true;
                gridViewTags.ResumeLayout();
                return;
            }

            RefreshSelectedImageTags();
        }

        internal void DeleteDatasetMediaFiles(IEnumerable<string> mediaPaths)
        {
            if (Program.DataManager == null || mediaPaths == null)
                return;

            List<string> deletedPaths = new List<string>();
            foreach (string file in mediaPaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(file))
                    continue;

                string tagFile = Path.Combine(Path.GetDirectoryName(file) ?? string.Empty, Path.GetFileNameWithoutExtension(file) + ".txt");
                try
                {
                    if (File.Exists(file))
                        File.Delete(file);
                    if (File.Exists(tagFile))
                        File.Delete(tagFile);
                    deletedPaths.Add(file);
                }
                catch (Exception ex)
                {
                    // Keep the item in the dataset if its file couldn't be deleted,
                    // so on-disk and in-memory state stay consistent.
                    Trace.WriteLine($"Failed to delete '{file}': {ex}");
                }
            }

            if (deletedPaths.Count > 0)
                Program.DataManager.RemoveMany(deletedPaths);

            RefreshDatasetGrid();
        }

        private void InitializeAiServerSetAndTestMenu()
        {
            menuAiServerSet = new ToolStripMenuItem { Name = "menuAiServerSet", Text = "AiServerSet" };
            menuAiServerSet.Click += (_, _) =>
            {
                using Form_AiServerSet form = new Form_AiServerSet(this);
                form.ShowDialog(this);
            };

            menuTestModule = new ToolStripMenuItem { Name = "menuTestModule", Text = "Test" };
            menuTestModule.Click += (_, _) =>
            {
                using Form_TestModule form = new Form_TestModule(this);
                form.ShowDialog(this);
            };

            menuLlmTagger = new ToolStripMenuItem { Name = "menuLlmTagger", Text = "LLM tagging" };
            menuLlmTagger.Click += (_, _) => ShowLlmTagger();

            menuVideoConvert = new ToolStripMenuItem { Name = "menuVideoConvert", Text = "Video convert" };
            menuVideoConvert.Click += (_, _) =>
            {
                using Form_VideoConvert form = new Form_VideoConvert(this);
                form.ShowDialog(this);
            };

            menuVideoExtract = new ToolStripMenuItem { Name = "menuVideoExtract", Text = "Frame extract" };
            menuVideoExtract.Click += (_, _) =>
            {
                using Form_VideoTools form = new Form_VideoTools(this);
                form.ShowDialog(this);
            };

            menuOnnxTagger = new ToolStripMenuItem { Name = "menuOnnxTagger", Text = "ONNX tagger" };
            menuOnnxTagger.Click += (_, _) => ShowOnnxTaggerForSelectedImages(autoRun: false);

            menuContextDSVideoTools = new ToolStripMenuItem { Name = "menuContextDSVideoTools", Text = "Video tools..." };
            menuContextDSVideoTools.Click += (_, _) =>
            {
                if (gridViewDS.SelectedRows.Count == 0)
                    return;

                string file = (string)gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value;
                if (!VideoProcessingService.IsVideoFile(file))
                    return;

                using Form_VideoTools form = new Form_VideoTools(this, file);
                form.ShowDialog(this);
            };

            menuContextDSRetagOnnx = new ToolStripMenuItem { Name = "menuContextDSRetagOnnx", Text = "Retag ONNX" };
            menuContextDSRetagOnnx.Click += (_, _) => ShowOnnxTaggerForSelectedImages(autoRun: true);

            menuContextDSRetagLlm = new ToolStripMenuItem { Name = "menuContextDSRetagLlm", Text = "LLM tagging" };
            menuContextDSRetagLlm.Click += (_, _) => ShowLlmTagger();

            contextMenuStrip1.Items.Add(menuContextDSRetagOnnx);
            contextMenuStrip1.Items.Add(menuContextDSRetagLlm);
            contextMenuStrip1.Items.Add(menuContextDSVideoTools);
            contextMenuStrip1.Opening += ContextMenuStrip1_Opening;

            toolsToolStripMenuItem.DropDownItems.Add(menuVideoConvert);
            toolsToolStripMenuItem.DropDownItems.Add(menuVideoExtract);
            toolsToolStripMenuItem.DropDownItems.Add(menuOnnxTagger);
            toolsToolStripMenuItem.DropDownItems.Add(backgroundRemovalWithRMBG20ToolStripMenuItem);
            toolsToolStripMenuItem.DropDownItems.Add(menuLlmTagger);

            int insertIndex = menuStrip1.Items.IndexOf(toolsToolStripMenuItem) + 1;
            if (insertIndex <= 0 || insertIndex > menuStrip1.Items.Count)
                insertIndex = menuStrip1.Items.Count;
            menuStrip1.Items.Insert(insertIndex, menuAiServerSet);
            menuStrip1.Items.Insert(insertIndex + 1, menuTestModule);
        }

        internal string GetSelectedDatasetDirectory()
        {
            if (!string.IsNullOrWhiteSpace(Program.DataManager?.DatasetRoot))
                return Program.DataManager.DatasetRoot;

            if (gridViewDS.SelectedRows.Count > 0)
            {
                string imagePath = (string)gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value;
                return Path.GetDirectoryName(imagePath);
            }

            return Program.DataManager.DataSet.Values
                .Select(item => Path.GetDirectoryName(item.ImageFilePath))
                .FirstOrDefault(path => !string.IsNullOrEmpty(path));
        }

        private static bool HasValidOpenAiAutoTagSettings()
        {
            string endpointText = Program.Settings.OpenAiAutoTagger.ConnectionAddress;
            return Uri.TryCreate(endpointText, UriKind.Absolute, out Uri endpoint)
                && (endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps)
                && !string.IsNullOrWhiteSpace(Program.Settings.OpenAiAutoTagger.ResolveVisionModel())
                && Program.OpenAiAutoTagger != null;
        }

        private bool EnsureOpenAiAutoTagConfigured(bool openAdvancedSettings)
        {
            if (!HasValidOpenAiAutoTagSettings())
            {
                MessageBox.Show(
                    this,
                    I18n.GetText("OpenAiAutoTagInvalidSettings"),
                    I18n.GetText("MenuAiServerSet"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                using Form_AiServerSet settings = new Form_AiServerSet(this);
                if (settings.ShowDialog(this) != DialogResult.OK || !HasValidOpenAiAutoTagSettings())
                    return false;
            }

            if (openAdvancedSettings)
            {
                using Form_AutoTaggerOpenAiSettings advancedSettings = new Form_AutoTaggerOpenAiSettings();
                if (advancedSettings.ShowDialog(this) != DialogResult.OK)
                    return false;
            }

            return true;
        }

        private bool EnsureSelectedAutoTagProviderConfigured(bool openAdvancedSettings)
        {
            if (!string.Equals(Program.Settings.AutoTagProviderId, "ai-api-server", StringComparison.OrdinalIgnoreCase))
                return EnsureOpenAiAutoTagConfigured(openAdvancedSettings);

            if (openAdvancedSettings || Program.Settings.AutoTagger.InterragatorParams.Count == 0)
            {
                using Form_AutoTaggerSettings settings = new Form_AutoTaggerSettings();
                if (settings.ShowDialog(this) != DialogResult.OK)
                    return false;
            }
            return Program.Settings.AutoTagger.InterragatorParams.Count > 0;
        }

        private async Task<(List<AiApiClient.AutoTagItem> data, string errorMessage, bool canceled)> GenerateWithSelectedAutoTagProviderAsync(
            string mediaPath)
        {
            string providerId = string.IsNullOrWhiteSpace(Program.Settings.AutoTagProviderId)
                ? "openai-compatible"
                : Program.Settings.AutoTagProviderId;
            IAutoTagProvider provider = Program.AutoTagProviders.GetRequired(providerId);
            AutoTagConnectionResult connection = await provider.ConnectAsync();
            if (!connection.Success)
                return (null, connection.ErrorMessage, false);
            IReadOnlyList<string> modelIds = string.Equals(providerId, "ai-api-server", StringComparison.OrdinalIgnoreCase)
                ? Program.Settings.AutoTagger.InterragatorParams.Keys.ToList()
                : new[] { Program.Settings.OpenAiAutoTagger.ResolveVisionModel() };
            AutoTagProviderResult result = await provider.GenerateAsync(new AutoTagProviderRequest
            {
                MediaPath = mediaPath,
                ModelIds = modelIds
            });
            List<AiApiClient.AutoTagItem> items = result.Items.Select(item =>
                new AiApiClient.AutoTagItem(item.Tag, item.Confidence)).ToList();
            return (result.Success ? items : null, result.ErrorMessage, result.Canceled);
        }

        private async Task<(List<AiApiClient.AutoTagItem> data, string errorMessage, bool canceled)> GenerateWithOpenAiAutoTaggerAsync(
            string mediaPath)
        {
            IAutoTagProvider provider = Program.AutoTagProviders.GetRequired("openai-compatible");
            AutoTagConnectionResult connection = await provider.ConnectAsync();
            if (!connection.Success)
                return (null, connection.ErrorMessage, false);
            AutoTagProviderResult result = await provider.GenerateAsync(new AutoTagProviderRequest
            {
                MediaPath = mediaPath,
                ModelIds = new[] { Program.Settings.OpenAiAutoTagger.ResolveVisionModel() }
            });
            List<AiApiClient.AutoTagItem> items = result.Items.Select(item =>
                new AiApiClient.AutoTagItem(item.Tag, item.Confidence)).ToList();
            return (result.Success ? items : null, result.ErrorMessage, result.Canceled);
        }

        private List<DataItem> GetSelectedImageDataItems()
        {
            var items = new List<DataItem>();
            if (Program.DataManager == null)
                return items;

            foreach (DataGridViewRow row in gridViewDS.SelectedRows)
            {
                string path = (string)row.Cells["ImageFilePath"].Value;
                if (VideoProcessingService.IsVideoFile(path))
                    continue;
                if (Program.DataManager.DataSet.TryGetValue(path, out DataItem item))
                    items.Add(item);
            }

            return items;
        }

        private void ShowLlmTagger()
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }

            using Form_LlmTagger form = new Form_LlmTagger(this);
            form.ShowDialog(this);
        }

        private void ShowOnnxTaggerForSelectedImages(bool autoRun)
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }

            if (autoRun && GetSelectedImageDataItems().Count == 0)
            {
                MessageBox.Show(I18n.GetText("TaggerNoImages"), I18n.GetText("UIError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using Form_OnnxTagger form = new Form_OnnxTagger(this, autoRun);
            form.ShowDialog(this);
        }

        internal bool TryQuickReplaceSelectedTag(int threshold)
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return false;
            }
            if (gridViewAllTags.SelectedCells.Count == 0)
            {
                MessageBox.Show(I18n.GetText("TipImgOrTagNotSelect"));
                return false;
            }

            int rowIndex = gridViewAllTags.SelectedCells[0].RowIndex;
            string selectedTag = (string)gridViewAllTags.Rows[rowIndex].Cells["TagsColumn"].Value;
            var sourceTags = QuickTagReplaceService.GetReplacementSourceTags(
                Program.DataManager.AllTags.Cast<AllTagsItem>(),
                selectedTag,
                threshold);

            if (sourceTags.Count == 0)
            {
                MessageBox.Show(I18n.GetText("TipQuickReplaceNoCandidates"));
                return false;
            }

            string message = string.Format(I18n.GetText("TipQuickReplaceConfirm"), sourceTags.Count, selectedTag);
            if (MessageBox.Show(message, I18n.GetText("TestQuickReplace"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return false;

            Program.DataManager.ReplaceTagsInAll(sourceTags, selectedTag);
            Program.Settings.QuickReplaceThreshold = threshold;
            Program.Settings.SaveSettings();
            LoadSelectedImageToGrid();
            return true;
        }

        internal void ReloadTranslationManagerForCurrentSettings()
        {
            string translationsDir = Path.Combine(Application.StartupPath, "Translations");
            if (!Directory.Exists(translationsDir))
                Directory.CreateDirectory(translationsDir);

            TranslationManager oldManager = Program.TransManager;
            Program.TransManager = Program.CreateTranslationManager(translationsDir);
            Program.TransManager.LoadTranslations();
            oldManager?.Dispose();
            Program.TagsList?.LoadTranslation(Program.TransManager);
        }

        private void CreateLangMenuItems()
        {
            foreach (var lang in I18n.GetLanguages())
            {
                ToolStripMenuItem menuItem = new ToolStripMenuItem();
                menuItem.Name = "btn_" + lang;
                menuItem.Text = I18n.GetText(menuItem.Name);
                menuItem.Click += LanguageXXBtn_Click;
                MenuLanguage.DropDownItems.Add(menuItem);
            }
        }

        private ContextMenuStrip tagContextMenu;
        private DataGridView tagContextGrid;
        private int tagContextRowIndex = -1;
        private string tagContextTag;

        private void InitializeTagContextMenu()
        {
            tagContextMenu = new ContextMenuStrip();

            ToolStripMenuItem queryWikiItem = new ToolStripMenuItem
            {
                Name = "TagContextQueryDanbooruWiki"
            };
            queryWikiItem.Click += TagContextQueryWiki_Click;

            ToolStripMenuItem retranslateItem = new ToolStripMenuItem
            {
                Name = "TagContextRetranslate"
            };
            retranslateItem.Click += TagContextRetranslate_Click;

            tagContextMenu.Items.AddRange(new ToolStripItem[]
            {
                queryWikiItem,
                retranslateItem
            });

            gridViewTags.CellMouseDown += TagGrid_CellMouseDown;
            gridViewAllTags.CellMouseDown += TagGrid_CellMouseDown;
            gridViewAutoTags.CellMouseDown += TagGrid_CellMouseDown;
        }

        private void UpdateTagContextMenuText()
        {
            if (tagContextMenu == null)
                return;

            tagContextMenu.Items["TagContextQueryDanbooruWiki"].Text = I18n.GetText("TagContextQueryDanbooruWiki");
            tagContextMenu.Items["TagContextRetranslate"].Text = I18n.GetText("TagContextRetranslate");
        }

        private void TagGrid_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0)
                return;

            DataGridView grid = (DataGridView)sender;
            string tag = GetTagFromGrid(grid, e.RowIndex);
            if (string.IsNullOrWhiteSpace(tag))
                return;

            if (e.ColumnIndex >= 0)
                grid.CurrentCell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
            grid.ClearSelection();
            grid.Rows[e.RowIndex].Selected = true;

            tagContextGrid = grid;
            tagContextRowIndex = e.RowIndex;
            tagContextTag = tag;
            tagContextMenu.Show(Cursor.Position);
        }

        private string GetTagFromGrid(DataGridView grid, int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count)
                return null;

            if (grid == gridViewTags && grid.DataSource is MultiSelectDataTable multiSelectTags)
            {
                if (rowIndex < multiSelectTags.Rows.Count)
                    return ((MultiSelectDataRow)multiSelectTags.Rows[rowIndex]).GetTagText();
            }

            if (grid == gridViewTags && grid.Columns.Contains("ImageTags"))
                return Convert.ToString(grid.Rows[rowIndex].Cells["ImageTags"].Value);

            if (grid == gridViewAllTags && grid.Columns.Contains("TagsColumn"))
                return Convert.ToString(grid.Rows[rowIndex].Cells["TagsColumn"].Value);

            if (grid == gridViewAutoTags && grid.Columns.Contains("Tag"))
                return Convert.ToString(grid.Rows[rowIndex].Cells["Tag"].Value);

            if (grid.Columns.Contains("Tag"))
                return Convert.ToString(grid.Rows[rowIndex].Cells["Tag"].Value);

            return null;
        }

        private void TagContextQueryWiki_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(tagContextTag))
                return;

            var popup = new Form_TagWikiPopup(tagContextTag);
            popup.Show(this);
        }

        private async void TagContextRetranslate_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(tagContextTag) || Program.TransManager == null)
                return;

            try
            {
                tagContextMenu.Enabled = false;
                SetStatus(I18n.GetText("StatusTranslating"));
                string result = await Program.TransManager.TranslateAsync(tagContextTag, forceRefresh: true);
                UpdateTagTranslation(tagContextGrid, tagContextRowIndex, tagContextTag, result);
                SetStatus(I18n.GetText("StatusRetranslated"));
            }
            catch
            {
                SetStatus(I18n.GetText("StatusTranslationComplete"));
            }
            finally
            {
                tagContextMenu.Enabled = true;
            }
        }

        private void UpdateTagTranslation(DataGridView grid, int rowIndex, string tag, string translation)
        {
            if (grid == null || rowIndex < 0)
                return;

            string result = translation ?? string.Empty;
            if (grid.Columns.Contains("Translation"))
                grid.Columns["Translation"].Visible = true;
            if (grid.Columns.Contains("TranslationColumn"))
                grid.Columns["TranslationColumn"].Visible = true;

            if (grid == gridViewTags && grid.DataSource is EditableTagList editableTags)
            {
                if (rowIndex < editableTags.Count && editableTags[rowIndex].Tag == tag)
                    editableTags[rowIndex].Translation = result;
            }
            else if (grid == gridViewTags && grid.DataSource is MultiSelectDataTable multiSelectTags)
            {
                foreach (MultiSelectDataRow row in multiSelectTags.Rows)
                {
                    if (row.RowState != DataRowState.Deleted
                        && row.GetTagText() == tag
                        && grid.Columns.Contains("Translation"))
                    {
                        row["Translation"] = result;
                    }
                }
            }
            else if (grid == gridViewAllTags)
            {
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (Convert.ToString(row.Cells["TagsColumn"].Value) == tag)
                    {
                        if (row.DataBoundItem is AllTagsItem item)
                            item.SetTranslation(result);
                        row.Cells["TranslationColumn"].Value = result;
                    }
                }
            }

            grid.Refresh();
        }



        private Form_filter allTagsFilter;

        private bool isAllTags = true;
        private bool isTranslate = false;
        private bool isFiltered = false;
        private bool showCount = false;

        private Form_preview fPreview;
        private bool isShowPreview = false;
        private PictureBox previewPicBox;
        private int previewRowIndex = -1;
        private FilterType filterAnd = FilterType.Or;
        private bool isLoading = false;
        private bool selectionMode = false;
        private HashSet<string> selectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);


        private void Form1_Load(object sender, EventArgs e)
        {
            Text += " " + Application.ProductVersion;
            gridViewDS.RowTemplate.Height = Program.Settings.PreviewSize + 10;
            gridViewAllTags.RowTemplate.Height = Program.Settings.GridViewRowHeight;
            gridViewTags.RowTemplate.Height = Program.Settings.GridViewRowHeight;
            gridViewTags.DefaultCellStyle.Font = Program.Settings.GridViewFont.GetFont();
            gridViewAllTags.DefaultCellStyle.Font = Program.Settings.GridViewFont.GetFont();
            gridViewDS.DefaultCellStyle.Font = Program.Settings.GridViewFont.GetFont();
            //splitContainer2.SplitterDistance = Width / 3;
            toolStrippromptFixedLengthComboBox.SelectedIndex = 0;
            if (!Program.Settings.FixTagsOnSaveLoad)
            {
                toolStripMenuItemWeight.Enabled = false;
                toolStripTextBoxWeight.Enabled = false;
            }
#if !DEBUG
            Extensions.CheckForUpdateAsync(Application.ProductVersion);
#else
            debugToolStripMenuItem.Visible = true;
#endif
        }

        private void ContextMenuImageGridHeader_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            ToolStripMenuItem tsi = (ToolStripMenuItem)e.ClickedItem;
            if (gridViewDS.Columns.Contains(tsi.Name))
            {
                int visibleCount = 0;
                for (int i = 0; i < gridViewDS.ColumnCount; i++)
                {
                    if (gridViewDS.Columns[i].Visible)
                        visibleCount++;
                }
                if (visibleCount > 1 || !tsi.Checked)
                {
                    tsi.Checked = !tsi.Checked;
                    gridViewDS.Columns[tsi.Name].Visible = tsi.Checked;
                }
                else
                {
                    MessageBox.Show(I18n.GetText("TipColumnMustVisible"));
                }
            }
        }

        private void ColorManager_SchemeChanded(object sender, EventArgs e)
        {
            if (Program.ColorManager.SelectedScheme != null)
            {
                Program.ColorManager.ChangeColorScheme(this, Program.ColorManager.SelectedScheme);
                Program.ColorManager.ChangeColorSchemeInConteiner(Controls, Program.ColorManager.SelectedScheme);
            }
        }

        private async void openFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await LoadFromFolderAsync(false);
        }

        private async void loadFolderWithAdditionalSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await LoadFromFolderAsync(true);
        }

        private async Task LoadFromFolderAsync(bool useAdditionalSettings)
        {
            if (Program.DataManager != null && Program.DataManager.IsDataSetChanged())
            {
                DialogResult result = MessageBox.Show(I18n.GetText("TipDSChangeSaveText"), I18n.GetText("TipDSChangeSaveTitle"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (result == DialogResult.Yes)
                {
                    Program.DataManager.SaveAll();
                    ReportSaveErrorsIfAny();
                }
            }
            OpenFolderDialog openFolderDialog = new OpenFolderDialog();
            if (openFolderDialog.ShowDialog() != DialogResult.OK)
                return;
            bool loadPreviewImages = true;
            bool readMetadata = false;
            if (useAdditionalSettings)
            {
                Form_LoadingSettings loadingSettings = new Form_LoadingSettings();
                if (loadingSettings.ShowDialog() != DialogResult.OK)
                {
                    loadingSettings.Close();
                    return;
                }
                loadPreviewImages = Program.Settings.LoadSettingsLoadPreviewImages;
                readMetadata = Program.Settings.LoadSettingsReadMetadata;
            }
            LoadingStatusText = I18n.GetText("TipLoadingStart");
            SetStatus(LoadingStatusText);
            LockEdit(true);
            isLoading = true;
            try
            {
                DatasetManager oldDataManager = Program.DataManager;
                Program.DataManager = new DatasetManager();
                if (oldDataManager != null)
                {
                    // Unbind before disposing so the grid cannot paint disposed bitmaps.
                    gridViewDS.DataSource = null;
                    oldDataManager.Dispose();
                }
                Program.DataManager.LoadingProgressChanged += DataManager_LoadingProgressChanged;
                TrackBarRowHeight.ValueChanged -= TrackBarRowHeight_ValueChanged;
                TrackBarRowHeight.TrackBar.Minimum = 1;
                TrackBarRowHeight.TrackBar.Maximum = Program.Settings.PreviewSize;
                TrackBarRowHeight.TrackBar.TickFrequency = 50;
                TrackBarRowHeight.TrackBar.SmallChange = 50;
                TrackBarRowHeight.TrackBar.LargeChange = 50;
                TrackBarRowHeight.Value = Program.Settings.PreviewSize;
                TrackBarRowHeight.ValueChanged += TrackBarRowHeight_ValueChanged;
                //Program.DataManager.SetTranslationMode(isTranslate);
                if (!await Program.DataManager.LoadFromFolderAsync(openFolderDialog.Folder, loadPreviewImages, readMetadata))
                {
                    SetStatus(I18n.GetText("TipFolderWrong"));
                    return;
                }
                gridViewDS.DataSource = Program.DataManager.GetDataSource();
                isAllTags = true;
                toolStripLabelAllTags.Text = I18n.GetText("UILabelAllTags");
                gridViewAllTags.DataSource = Program.DataManager.AllTagsBindingSource;
                ApplyDataSetGridStyle();
                await ApplyTranslation(isTranslate);
                gridViewDS.AutoResizeColumns();
                SetStatus(I18n.GetText("TipLoadingComplete"));
                SetDSCountStatus(string.Format(I18n.GetText("LabelShownDsImages"), gridViewDS.RowCount, Program.DataManager.DataSet.Count));
            }
            catch (Exception ex)
            {
                // A locked file / unreadable subfolder inside the parallel loader
                // surfaces here as an AggregateException; without this catch it
                // escaped the async void menu handlers with the UI left locked.
                Program.LogCrash(ex, "LoadFromFolder", terminating: false);
                SetStatus(I18n.GetText("TipFolderWrong"));
                MessageBox.Show(this, ex.InnerException?.Message ?? ex.Message,
                    "BooruDatasetTagManagerPlus", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                isLoading = false;
                LockEdit(false);
            }
        }

        //This is necessary to speed up the work, since searching for a string using I18n.GetText takes a long time.
        private string LoadingStatusText = "";
        private void DataManager_LoadingProgressChanged(int current, int max)
        {
            // Raised from the parallel loader's worker threads: marshal onto the
            // UI thread instead of touching the status strip cross-thread.
            if (IsDisposed || !IsHandleCreated)
                return;
            try
            {
                BeginInvoke(new Action(() => SetStatus($"{LoadingStatusText} ({current}/{max})")));
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void TrackBarRowHeight_ValueChanged(object sender, EventArgs e)
        {
            gridViewDS.SuspendLayout();
            if (gridViewDS.AutoSizeRowsMode == DataGridViewAutoSizeRowsMode.AllCells)
            {
                gridViewDS.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            }
            //if (gridViewDS.AutoSizeColumnsMode == DataGridViewAutoSizeColumnsMode.AllCells)
            //{
            //    gridViewDS.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            //}
            //for (int i = 0; i < gridViewDS.ColumnCount; i++)
            //{
            //    if (gridViewDS.Columns[i].ValueType == typeof(Image))
            //    {
            //        gridViewDS.Columns[i].Width = TrackBarRowHeight.Value;
            //        //((DataGridViewImageColumn)gridViewDS.Columns[i]).Width = TrackBarRowHeight.Value;
            //    }
            //}
            for (int i = 0; i < gridViewDS.RowCount; i++)
            {
                gridViewDS.Rows[i].Height = TrackBarRowHeight.Value;
            }
            gridViewDS.ResumeLayout();
        }

        private async Task FillTranslation(DataGridView grid)
        {
            string transColumnName = string.Empty;
            if (grid.Columns.Contains("Translation"))
            {
                transColumnName = "Translation";
            }
            else if (grid.Columns.Contains("TranslationColumn"))
            {
                transColumnName = "TranslationColumn";
            }
            else
                return;
            if (grid.Columns[transColumnName].Visible == false)
            {
                grid.Columns[transColumnName].Visible = true;
            }
            SetStatus(I18n.GetText("StatusTranslating"));
            try
            {
                if (grid == gridViewAllTags && Program.DataManager != null)
                {
                    await Program.DataManager.AllTags.TranslateAllAsync();
                }
                else if (grid.DataSource is EditableTagList editableTags)
                {
                    await editableTags.TranslateAllAsync();
                }
                else if (grid.DataSource is MultiSelectDataTable multiSelectTags)
                {
                    await multiSelectTags.TranslateAllAsync();
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                SetStatus(I18n.GetText("StatusTranslationComplete"));
            }
        }

        internal void LockEdit(bool locked)
        {
            menuStrip1.Enabled = !locked;
            toolStripTags.Enabled = !locked;
            toolStripAllTags.Enabled = !locked;
            gridViewTags.Enabled = !locked;
            if (gridViewTags.SelectedRows.Count == 1)
                gridViewTags.AllowDrop = !locked;
            gridViewAllTags.Enabled = !locked;
            gridViewDS.Enabled = !locked;
            gridViewAutoTags.Enabled = !locked;
            toolStripAutoTags.Enabled = !locked;
        }

        private void ShowPreview(string imgPath, bool separateWindow = false)
        {
            if (VideoProcessingService.IsVideoFile(imgPath))
            {
                if (separateWindow || Program.Settings.PreviewType == ImagePreviewType.SeparateWindow)
                {
                    Process.Start(new ProcessStartInfo(imgPath) { UseShellExecute = true });
                    return;
                }

                if (Program.Settings.PreviewType == ImagePreviewType.PreviewInMainWindow)
                {
                    int previewSize = Math.Max(Program.Settings.PreviewSize, 320);
                    Image videoPreview = Extensions.MakeVideoThumb(imgPath, previewSize, drawBadge: false);
                    if (videoPreview == null)
                        return;
                    SetPreviewImage(videoPreview);
                }
                return;
            }
            Image img = Program.DataManager.GetImageFromFileWithCache(imgPath);
            if (img == null)
                return;
            if (separateWindow || Program.Settings.PreviewType == ImagePreviewType.SeparateWindow)
            {
                if (fPreview == null || fPreview.IsDisposed)
                    fPreview = new Form_preview();
                fPreview.Show(img);
            }
            else if (Program.Settings.PreviewType == ImagePreviewType.PreviewInMainWindow)
            {
                SetPreviewImage(img);
            }
        }

        /// <summary>
        /// Swaps the main preview image, always detaching the previous image from the
        /// PictureBox before disposing it. Assigning a new image (or null) while the
        /// old one is still attached and then disposing it can leave the control
        /// animating a disposed image on the next WM_SHOWWINDOW, which throws
        /// "Parameter is not valid" from ImageAnimator.CanAnimate.
        /// </summary>
        private void SetPreviewImage(Image img)
        {
            // GetImageFromFileWithCache always hands out a caller-owned instance
            // (the cache keeps its own clone), so the old image must be disposed
            // regardless of the CacheOpenImages setting.
            Image old = pictureBoxPreview.Image;
            pictureBoxPreview.Image = img;
            if (old != null && !ReferenceEquals(old, img))
                old.Dispose();
        }

        private void HidePreview()
        {
            fPreview?.Hide();
            SetPreviewImage(null);
        }

        private async void LoadSelectedImageToGrid()
        {
            gridViewTags.AutoGenerateColumns = false;
            if (gridViewDS.SelectedRows.Count == 0)
            {
                BtnTagImageChecker.Enabled = true;
                return;
            }
            gridViewTags.SuspendLayout();
            try
            {
                if (gridViewDS.SelectedRows.Count == 1)
                {
                    BtnTagImageChecker.Enabled = false;
                    string selectedPath = gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value as string;
                    // Guard against a stale/placeholder row (null path) or an item that
                    // was just removed from the dataset, so we never pass a null key to
                    // the DataSet dictionary.
                    if (string.IsNullOrEmpty(selectedPath)
                        || !Program.DataManager.DataSet.TryGetValue(selectedPath, out DataItem selectedItem))
                    {
                        gridViewTags.DataSource = null;
                        gridViewTags.AllowDrop = false;
                        return;
                    }
                    gridViewTags.Tag = selectedPath;
                    ChageImageColumn(false);
                    gridViewTags.DataSource = selectedItem.Tags;
                    if (isShowPreview)
                    {
                        ShowPreview(selectedPath);
                    }
                    gridViewTags.AllowDrop = true;
                }
                else
                {
                    BtnTagImageChecker.Enabled = true;
                    if (isShowPreview)
                    {
                        HidePreview();
                    }
                    gridViewTags.DataSource = null;
                    gridViewTags.AllowDrop = false;
                    gridViewTags.Rows.Clear();
                    ChageImageColumn(true);

                    gridViewTags.Tag = "0";
                    List<DataItem> selectedTagsList = new List<DataItem>();
                    for (int i = 0; i < gridViewDS.SelectedRows.Count; i++)
                    {
                        string path = gridViewDS.SelectedRows[i].Cells["ImageFilePath"].Value as string;
                        // Skip stale/removed rows so a null or missing key can't crash the grid.
                        if (!string.IsNullOrEmpty(path)
                            && Program.DataManager.DataSet.TryGetValue(path, out DataItem multiItem))
                        {
                            selectedTagsList.Add(multiItem);
                        }
                    }

                    MultiSelectDataTable multiSelectData = new MultiSelectDataTable();
                    multiSelectData.SetTranslationMode(isTranslate);
                    await multiSelectData.CreateTableFromSelectedImages(selectedTagsList);
                    gridViewTags.DataSource = multiSelectData;
                }

                if (Program.Settings.AutoSort)
                {
                    SortPrompt();
                }

                gridViewDS.Focus();
                if (isTranslate)
                {
                    var dsType = GetTagsDataSourceType();
                    if (dsType == DataSourceType.Single)
                    {
                        await ((EditableTagList)gridViewTags.DataSource).TranslateAllAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                // Runs on every selection change (async void): a translation/network
                // failure must not escape as an unhandled exception.
                Trace.WriteLine($"LoadSelectedImageToGrid failed: {ex}");
                SetStatus(ex.Message);
            }
            finally
            {
                gridViewTags.ResumeLayout();
            }
            //await FillTranslation(gridViewTags);
        }

        /// <summary>
        /// Add or remove Image column
        /// </summary>
        /// <param name="add"> true to add, false to remove</param>
        private void ChageImageColumn(bool add)
        {
            gridViewTags.Columns["ImageName"].Visible = add;
        }

        private Rectangle dragBoxFromMouseDown;
        private int rowIndexFromMouseDown;
        private int rowIndexOfItemUnderMouseToDrop;
        private void dataGridView1_MouseMove(object sender, MouseEventArgs e)
        {
            if ((e.Button & MouseButtons.Left) == MouseButtons.Left)
            {
                // If the mouse moves outside the rectangle, start the drag.
                if (dragBoxFromMouseDown != Rectangle.Empty &&
                    !dragBoxFromMouseDown.Contains(e.X, e.Y))
                {

                    // Proceed with the drag and drop, passing in the list item.                    
                    DragDropEffects dropEffect = gridViewTags.DoDragDrop(
                    gridViewTags.Rows[rowIndexFromMouseDown],
                    DragDropEffects.Move);
                }
            }
        }

        private void dataGridView1_MouseDown(object sender, MouseEventArgs e)
        {
            // Get the index of the item the mouse is below.
            rowIndexFromMouseDown = gridViewTags.HitTest(e.X, e.Y).RowIndex;
            if (rowIndexFromMouseDown != -1)
            {
                // Remember the point where the mouse down occurred. 
                // The DragSize indicates the size that the mouse can move 
                // before a drag event should be started.                
                Size dragSize = SystemInformation.DragSize;

                // Create a rectangle using the DragSize, with the mouse position being
                // at the center of the rectangle.
                dragBoxFromMouseDown = new Rectangle(new Point(e.X - (dragSize.Width / 2),
                                                               e.Y - (dragSize.Height / 2)),
                                    dragSize);
            }
            else
                // Reset the rectangle if the mouse is not over an item in the ListBox.
                dragBoxFromMouseDown = Rectangle.Empty;
        }

        private void dataGridView1_DragOver(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Move;
        }

        private void dataGridView1_DragDrop(object sender, DragEventArgs e)
        {
            // The mouse locations are relative to the screen, so they must be 
            // converted to client coordinates.
            Point clientPoint = gridViewTags.PointToClient(new Point(e.X, e.Y));

            // Get the row index of the item the mouse is below. 
            rowIndexOfItemUnderMouseToDrop =
                gridViewTags.HitTest(clientPoint.X, clientPoint.Y).RowIndex;

            // If the drag operation was a move then remove and insert the row.
            if (e.Effect == DragDropEffects.Move)
            {
                if (rowIndexFromMouseDown != rowIndexOfItemUnderMouseToDrop)
                {
                    int destIndex = ((EditableTagList)gridViewTags.DataSource).Move(rowIndexFromMouseDown, rowIndexOfItemUnderMouseToDrop);
                    gridViewTags.ClearSelection();
                    gridViewTags[0, destIndex].Selected = true;
                }
            }
        }

        private void BtnAddTag_Clicked(object sender, EventArgs e)
        {
            AddNewRow();
        }

        private void AddNewRow()
        {
            if (gridViewTags.DataSource == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }
            if (gridViewDS.SelectedRows.Count > 1)
            {
                using (Form_addTag addTag = new Form_addTag())
                {
                    if (gridViewTags.SelectedCells.Count > 0)
                    {
                        addTag.tagTextBox.Text = ((MultiSelectDataRow)((MultiSelectDataTable)gridViewTags.DataSource).Rows[gridViewTags.SelectedCells[0].RowIndex]).GetTagText();
                        addTag.tagTextBox.SelectAll();
                    }
                    if (addTag.ShowDialog() == DialogResult.OK)
                    {
                        AddingType addType = Extensions.GetEnumItemFromFriendlyText<DatasetManager.AddingType>((string)addTag.comboBox1.SelectedItem);
                        int customIndex = (int)addTag.numericUpDown1.Value;
                        bool skipExist = addTag.checkBoxSkipExist.Checked;
                        AddTagMultiselectedMode(addTag.tagTextBox.Text, skipExist, addType, customIndex);
                    }
                    addTag.Close();
                }
            }
            else
            {
                if (gridViewTags.SelectedCells.Count == 0 || gridViewTags.RowCount == 0)
                {
                    ((EditableTagList)gridViewTags.DataSource).AddNew();
                    SetDGVSelection(gridViewTags, gridViewTags.RowCount - 1, "ImageTags");
                }
                else
                {
                    int index = GetFirstDGVSelectionIndex(gridViewTags);
                    ((EditableTagList)gridViewTags.DataSource).InsertNew(gridViewTags.SelectedCells[0].RowIndex + 1);
                    SetDGVSelection(gridViewTags, index + 1, "ImageTags");
                }
            }
        }

        private void SetDGVSelection(DataGridView dgv, int index, string column)
        {
            dgv.ClearSelection();
            dgv[column, index].Selected = true;
        }

        private int GetFirstDGVSelectionIndex(DataGridView dgv)
        {
            if (dgv.SelectedCells.Count == 0)
                return -1;
            return dgv.SelectedCells[0].RowIndex;
        }

        private void BtnTagDelete_Click(object sender, EventArgs e)
        {
            if (gridViewTags.SelectedCells.Count == 0)
                return;
            gridViewTags.Rows.RemoveAt(gridViewTags.SelectedCells[0].RowIndex);
        }

        private void toolStripButton4_Click(object sender, EventArgs e)
        {
            if (gridViewTags.SelectedCells.Count == 0 || gridViewTags.SelectedCells[0].RowIndex == 0)
                return;
            int curIndex = gridViewTags.SelectedCells[0].RowIndex;
            int destIndex = ((EditableTagList)gridViewTags.DataSource).Move(curIndex, curIndex - 1);
            gridViewTags.ClearSelection();
            gridViewTags["ImageTags", destIndex].Selected = true;
        }

        private void toolStripButton5_Click(object sender, EventArgs e)
        {
            if (gridViewTags.SelectedCells.Count == 0 || gridViewTags.SelectedCells[0].RowIndex == gridViewTags.RowCount - 1)
                return;
            int curIndex = gridViewTags.SelectedCells[0].RowIndex;
            int destIndex = ((EditableTagList)gridViewTags.DataSource).Move(curIndex, curIndex + 1);
            gridViewTags["ImageTags", destIndex].Selected = true;
        }

        private void toolStripButton6_Click(object sender, EventArgs e)
        {
            isAllTags = !isAllTags;
            if (isAllTags)
            {
                toolStripLabelAllTags.Text = I18n.GetText("UILabelAllTags");
                Program.DataManager.AllTagsBindingSource.RemoveFilter();
            }
            else
            {
                toolStripLabelAllTags.Text = I18n.GetText("UILabelCommonTags");
                Program.DataManager.AllTags.SetFilterByCount(Program.DataManager.DataSet.Count);
            }
        }

        private void BtnAddTagForAll_Click(object sender, EventArgs e)
        {
            AddTagToAll(false);
        }

        private async void AddTagToAll(bool filtered)
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }
            Form_addTag addTag = new Form_addTag();
            int index = gridViewAllTags.RowCount;
            if (gridViewAllTags.SelectedCells.Count > 0)
            {
                index = gridViewAllTags.SelectedCells[0].RowIndex;
                addTag.tagTextBox.Text = (string)gridViewAllTags.Rows[index].Cells[0].Value;
                addTag.tagTextBox.SelectAll();
            }
            if (addTag.ShowDialog() == DialogResult.OK)
            {
                int customIndex = (int)addTag.numericUpDown1.Value;
                bool skipExist = addTag.checkBoxSkipExist.Checked;
                DatasetManager.AddingType addType = Extensions.GetEnumItemFromFriendlyText<DatasetManager.AddingType>((string)addTag.comboBox1.SelectedItem);
                Program.DataManager.AddTagToAll(addTag.tagTextBox.Text, skipExist, addType, customIndex, filtered);
                if (gridViewDS.SelectedRows.Count == 1)
                {
                    if (isTranslate)
                    {
                        await ((EditableTagList)gridViewTags.DataSource).TranslateAllAsync();
                    }
                }
                else
                {
                    AddTagMultiselectedMode(addTag.tagTextBox.Text, skipExist, addType, customIndex);
                }
            }
            addTag.Close();
        }

        private void toolStripButton8_Click(object sender, EventArgs e)
        {
            if (gridViewDS.SelectedRows.Count != 1)
            {
                MessageBox.Show(I18n.GetText("TipReplaceNotSupportMultSel"));
                return;
            }

            if (gridViewAllTags.SelectedCells.Count == 0)
                return;
            Form_replaceAll replaceAll = new Form_replaceAll();
            replaceAll.DataSetFiltered = isFiltered;
            replaceAll.comboBox1.DataSource = Program.DataManager.AllTags;
            replaceAll.comboBox1.DisplayMember = "Tag";
            List<int> selectedCells = new List<int>();
            for (int i = 0; i < gridViewAllTags.SelectedCells.Count; i++)
            {
                if (!selectedCells.Contains(gridViewAllTags.SelectedCells[i].RowIndex))
                    selectedCells.Add(gridViewAllTags.SelectedCells[i].RowIndex);
            }
            selectedCells.Reverse();
            replaceAll.comboBox1.SelectedIndex = selectedCells[0];
            replaceAll.SetNewTagValues(Program.DataManager.AllTags.GetAllTagsList());
            if (selectedCells.Count > 1)
            {
                replaceAll.SetNewTagText((string)gridViewAllTags["TagsColumn", selectedCells[1]].Value);
            }
            if (replaceAll.ShowDialog() == DialogResult.OK)
            {
                Program.DataManager.ReplaceTagInAll(
                    ((AllTagsItem)replaceAll.comboBox1.SelectedItem).Tag,
                    replaceAll.NewTagText,
                    replaceAll.DataSetFiltered);
            }
            replaceAll.Close();
        }

        private void saveAllChangesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }
            Program.DataManager.SaveAll();
            Program.DataManager.UpdateDatasetHash();
            if (!ReportSaveErrorsIfAny())
                SetStatus(I18n.GetText("StatusSaved"));
            LoadSelectedImageToGrid();
        }

        private void showPreviewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }
            isShowPreview = !isShowPreview;
            MenuShowPreview.Checked = isShowPreview;
            if (isShowPreview)
            {
                //tabPreview.Show();
                if (gridViewDS.SelectedRows.Count == 1)
                {
                    ShowPreview((string)gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value);
                }
                else
                {
                    HidePreview();
                }
            }
            else
            {
                HidePreview();
            }
        }

        private void toolStripButton9_Click(object sender, EventArgs e)
        {
            if (gridViewDS.SelectedRows.Count == 1)
            {
                List<string> tagsToCopy = new List<string>();
                for (int i = 0; i < gridViewTags.RowCount; i++)
                {
                    tagsToCopy.Add((string)gridViewTags["ImageTags", i].Value);
                }
                try
                {
                    Clipboard.SetData("TagList", tagsToCopy);
                    SetStatus(I18n.GetText("StatusCopied"));
                }
                catch (System.Runtime.InteropServices.ExternalException ex)
                {
                    // Clipboard busy in another process.
                    SetStatus(ex.Message);
                }
            }
            else if (gridViewDS.SelectedRows.Count > 1)
            {
                MessageBox.Show(I18n.GetText("TipMultiImageCopy"));
            }
            else
            {
                MessageBox.Show(I18n.GetText("TipSelectImage"));
            }
        }

        internal void SetStatus(string text)
        {
            statusLabel.Text = text;
        }

        /// <summary>
        /// Shows the per-file failures collected by the last DataManager.SaveAll().
        /// Returns true when there were failures (items stay marked modified).
        /// </summary>
        private bool ReportSaveErrorsIfAny()
        {
            var errors = Program.DataManager?.LastSaveErrors;
            if (errors == null || errors.Count == 0)
                return false;
            string details = string.Join("\n", errors.Take(10));
            if (errors.Count > 10)
                details += "\n...";
            MessageBox.Show(this, string.Format(I18n.GetText("TipSaveErrors"), errors.Count, details),
                "BooruDatasetTagManagerPlus", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return true;
        }

        private void SetDSCountStatus(string text)
        {
            toolStripLabelDSShown.Text = text;
        }

        private async void BtnPasteTag_Click(object sender, EventArgs e)
        {
            if (gridViewDS.SelectedRows.Count == 1)
            {
                if (Clipboard.ContainsData("TagList"))
                {
                    // "as" instead of a hard cast: foreign clipboard content under
                    // the same format name must not throw InvalidCastException.
                    List<string> copiedTags = Clipboard.GetData("TagList") as List<string>;
                    if (copiedTags != null && copiedTags.Count > 0)
                    {
                        var eTagList = (EditableTagList)gridViewTags.DataSource;
                        eTagList.Clear();
                        eTagList.AddRange(copiedTags, true);
                        if (isTranslate)
                            await FillTranslation(gridViewTags);
                        SetStatus(I18n.GetText("StatusPasted"));
                    }
                    else
                    {
                        MessageBox.Show(I18n.GetText("TipClipboardEmpty"));
                    }
                }
                else
                {
                    MessageBox.Show(I18n.GetText("TipClipboardEmpty"));
                }
            }
            else if (gridViewDS.SelectedRows.Count > 1)
            {
                MessageBox.Show(I18n.GetText("TipMultiImagePaste"));
            }
            else
            {
                MessageBox.Show(I18n.GetText("TipSelectImage"));
            }
        }

        private async void toolStripButton11_Click(object sender, EventArgs e)
        {
            await ApplyTagHistoryActionAsync(tags => tags.PrevState());
        }

        private void BtnDeleteTagForAll_Click(object sender, EventArgs e)
        {
            RemoveTagFromAll(false);
        }

        private void RemoveTagFromAll(bool filtered)
        {
            HashSet<string> tagsToDelete = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < gridViewAllTags.SelectedCells.Count; i++)
            {
                var row = gridViewAllTags.SelectedCells[i].RowIndex;
                string tag = (string)gridViewAllTags.Rows[row].Cells["TagsColumn"].Value;
                if (!string.IsNullOrWhiteSpace(tag))
                    tagsToDelete.Add(tag);
            }

            if (tagsToDelete.Count == 0)
                return;

            LockEdit(true);
            gridViewAllTags.SuspendLayout();
            gridViewTags.SuspendLayout();
            try
            {
                Program.DataManager.DeleteTagsFromAll(tagsToDelete, filtered);
                if (gridViewDS.SelectedRows.Count > 1)
                    LoadSelectedImageToGrid();
            }
            finally
            {
                gridViewTags.ResumeLayout();
                gridViewAllTags.ResumeLayout();
                LockEdit(false);
            }
        }

        private async void translateTagsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            isTranslate = !isTranslate;
            await ApplyTranslation(isTranslate);
        }

        private async Task ApplyTranslation(bool needTranslate)
        {
            MenuItemTranslateTags.Checked = needTranslate;
            if (Program.DataManager != null)
            {
                Program.DataManager.SetTranslationMode(needTranslate);
            }
            if (needTranslate)
            {
                await FillTranslation(gridViewAllTags);
                await FillTranslation(gridViewTags);
            }
            else
            {
                // The columns only exist after a translation pass; toggling the
                // menu before that dereferenced a null column.
                if (gridViewTags.Columns.Contains("Translation"))
                    gridViewTags.Columns["Translation"].Visible = false;
                if (gridViewAllTags.Columns.Contains("TranslationColumn"))
                    gridViewAllTags.Columns["TranslationColumn"].Visible = false;
            }
        }

        //private int findIndex = -1;
        private void toolStripButton13_Click(object sender, EventArgs e)
        {
            SetFilter();
        }

        private HashSet<string> GetSelectedTags()
        {
            HashSet<string> findTags = new HashSet<string>();
            for (int i = 0; i < gridViewAllTags.SelectedCells.Count; i++)
            {
                int row = gridViewAllTags.SelectedCells[i].RowIndex;
                string value = (string)gridViewAllTags.Rows[row].Cells[0].Value;
                if (!findTags.Contains(value))
                    findTags.Add(value);
            }
            return findTags;
        }

        private void SaveSelectedInViewDs()
        {
            selectedFiles.Clear();
            for (int i = 0; i < gridViewDS.SelectedRows.Count; i++)
            {
                selectedFiles.Add((string)gridViewDS.SelectedRows[i].Cells["ImageFilePath"].Value);
            }
        }

        private void LoadSelectedInViewDs()
        {
            gridViewDS.ClearSelection();
            bool foundSelected = false;
            int firstDisplayed = 0;
            for (int i = 0; i < gridViewDS.RowCount; i++)
            {
                if (selectedFiles.Contains((string)gridViewDS["ImageFilePath", i].Value))
                {
                    if (firstDisplayed == 0)
                        firstDisplayed = i;
                    gridViewDS.Rows[i].Selected = true;
                    foundSelected = true;
                }
            }
            if (!foundSelected && gridViewDS.RowCount > 0)
            {
                gridViewDS.Rows[0].Selected = true;
            }
            // Will throw an exception by itself if there is nothing found due to being set to -1 internally when the list is loaded in empty. Lazy bypass of 
            try
            {
                gridViewDS.FirstDisplayedScrollingRowIndex = firstDisplayed;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        private void SetFilter()
        {
            isLoading = true;
            if (gridViewAllTags.SelectedCells.Count > 0)
            {
                SaveSelectedInViewDs();
                if (isFiltered)
                {
                    ResetFilter();
                }

                gridViewDS.DataSource = Program.DataManager.GetDataSource(DatasetManager.OrderType.Name, filterAnd, GetSelectedTags());
                if (gridViewDS.RowCount == 0)
                    gridViewTags.DataSource = null;
                isFiltered = true;
                LoadSelectedInViewDs();
                BtnImageExitFilter.Enabled = true;
            }
            isLoading = false;
            SetDSCountStatus(string.Format(I18n.GetText("LabelShownDsImages"), gridViewDS.RowCount, Program.DataManager.DataSet.Count));
        }

        private void ResetFilter()
        {
            isLoading = true;
            if (isFiltered)
            {
                SaveSelectedInViewDs();
                gridViewDS.DataSource = Program.DataManager.GetDataSource();
                isFiltered = false;
                BtnImageExitFilter.Enabled = false;
                LoadSelectedInViewDs();
            }
            isLoading = false;
            SetDSCountStatus(string.Format(I18n.GetText("LabelShownDsImages"), gridViewDS.RowCount, Program.DataManager.DataSet.Count));
        }

        private void toolStripButton14_Click(object sender, EventArgs e)
        {
            ResetFilter();
        }

        private async void dataGridView1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                BtnTagDelete.PerformClick();
            }
            else if (e.KeyCode == Keys.Insert)
            {
                BtnTagAdd.PerformClick();
            }
            else if (e.Control && e.KeyCode == Keys.V)
            {
                await PasteTagsFromClipboard();
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.C)
            {
                if (gridViewTags.CurrentCell != null && !gridViewTags.CurrentCell.IsInEditMode)
                {
                    List<string> tagsToCopy = new List<string>();
                    tagsToCopy.Add((string)gridViewTags["ImageTags", gridViewTags.CurrentCell.RowIndex].Value);
                    DataObject d = new DataObject();
                    d.SetText(string.Join("\r\n", tagsToCopy));
                    d.SetData("PartTagList", tagsToCopy);
                    try
                    {
                        Clipboard.SetDataObject(d);
                        SetStatus(I18n.GetText("StatusCopied"));
                    }
                    catch (System.Runtime.InteropServices.ExternalException ex)
                    {
                        SetStatus(ex.Message);
                    }
                    e.SuppressKeyPress = true;
                }
            }
        }

        private async Task PasteTagsFromClipboard()
        {
            if (Clipboard.ContainsData("PartTagList"))
            {
                List<string> copiedTags = Clipboard.GetData("PartTagList") as List<string>;
                if (copiedTags != null && copiedTags.Count > 0)
                {
                    if (gridViewDS.SelectedRows.Count == 1)
                    {

                        var eTagList = (EditableTagList)gridViewTags.DataSource;
                        copiedTags.ForEach(a => eTagList.AddTag(a, true, AddingType.Down));
                        if (isTranslate)
                            await FillTranslation(gridViewTags);
                        SetStatus(I18n.GetText("StatusPasted"));

                    }
                    else if (gridViewDS.SelectedRows.Count > 1)
                    {
                        foreach (var t in copiedTags)
                        {
                            AddTagMultiselectedMode(t, true, AddingType.Down);
                        }
                    }
                }
            }
            else
            {
                SetStatus(I18n.GetText("TipClipboardEmpty"));
            }
        }

        private void loadLossFromFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
        }

        private async void toolStripButton15_Click(object sender, EventArgs e)
        {
            if (Program.DataManager == null)
            {
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText();
                    var lines = PromptParser.ParsePrompt(text, Program.Settings.FixTagsOnSaveLoad, Program.Settings.SeparatorOnLoad);
                    EditableTagList tagList = new EditableTagList();
                    tagList.LoadFromPromptParserData(lines);
                    gridViewTags.DataSource = tagList;
                }
                //MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                //return;
            }
            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText();
                var lines = PromptParser.ParsePrompt(text, Program.Settings.FixTagsOnSaveLoad, Program.Settings.SeparatorOnLoad);
                var tagsDSType = GetTagsDataSourceType();
                if (tagsDSType == DataSourceType.Single)
                {
                    EditableTagList etl = (EditableTagList)gridViewTags.DataSource;
                    etl.Clear();
                    etl.AddRange(lines, true);
                }
                if (isTranslate)
                    await FillTranslation(gridViewTags);
            }
        }

        private DataSourceType GetTagsDataSourceType()
        {
            if (gridViewTags.DataSource == null)
                return DataSourceType.None;
            else if (gridViewTags.DataSource.GetType() == typeof(EditableTagList))
                return DataSourceType.Single;
            else if (gridViewTags.DataSource.GetType() == typeof(MultiSelectDataTable))
                return DataSourceType.Multi;
            else
                throw new Exception("Unknown datasource type!");
        }

        private void toolStripButton16_Click(object sender, EventArgs e)
        {
            var dts = GetTagsDataSourceType();
            if (dts == DataSourceType.Single)
            {
                Form_Edit fPrint = new Form_Edit();
                EditableTagList tagsDS = (EditableTagList)gridViewTags.DataSource;
                fPrint.textBox1.Text = tagsDS.ToString();
                fPrint.Show();
            }
        }

        private void dataGridView1_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            if (gridViewTags.CurrentCell.ColumnIndex == 0)
            {
                AutoCompleteTextBox autoText = e.Control as AutoCompleteTextBox;
                if (autoText != null)
                {
                    //autoText.SetParent(gridViewTags);
                    if (Program.Settings.AutocompleteMode != AutocompleteMode.Disable && autoText.Values == null)
                    {
                        autoText.SetAutocompleteMode(Program.Settings.AutocompleteMode, Program.Settings.AutocompleteSort);
                        autoText.Values = Program.TagsList.Tags;
                    }
                    //autoText.Location = new Point(10, 10);
                    //autoText.Size = new Size(25, 75);
                    //autoText.AutoCompleteMode = AutoCompleteMode.Suggest;
                    //autoText.AutoCompleteSource = AutoCompleteSource.CustomSource;
                    //autoText.AutoCompleteCustomSource = Program.TagsList.Tags;
                }
            }
        }

        private void toolStripButton17_Click(object sender, EventArgs e)
        {
            if (gridViewDS.SelectedRows.Count != 1)
            {
                MessageBox.Show("Select one image!");
                return;
            }
            if (GetTagsDataSourceType() != DataSourceType.Single)
            {
                SetStatus(I18n.GetText("TipMultiImagePaste"));
                return;
            }
            EditableTagList clonedTagList = (EditableTagList)((EditableTagList)gridViewTags.DataSource).Clone();
            switch (MessageBox.Show(I18n.GetText("TipSetToAllText"), I18n.GetText("TipSetToAllTitle"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
            {
                case DialogResult.Yes:
                    Program.DataManager.SetTagListToAll(clonedTagList, true);
                    break;
                case DialogResult.No:
                    Program.DataManager.SetTagListToAll(clonedTagList, false);
                    break;
                case DialogResult.Cancel:
                    return;
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (Program.DataManager != null && Program.DataManager.IsDataSetChanged())
            {
                DialogResult result = MessageBox.Show(I18n.GetText("TipDSChangeSaveText"), I18n.GetText("TipDSChangeSaveTitle"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (result == DialogResult.Yes)
                {
                    Program.DataManager.SaveAll();
                    ReportSaveErrorsIfAny();
                }
                else if (result == DialogResult.Cancel)
                    e.Cancel = true;
            }
        }

        private void dataGridView2_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex == -1 || e.ColumnIndex == -1)
                return;
            AddSelectedAllTagsToImageTags();
        }

        private void dataGridView3_DataSourceChanged(object sender, EventArgs e)
        {

        }

        private void ApplyDataSetGridStyle()
        {
            for (int i = 0; i < gridViewDS.ColumnCount; i++)
            {
                if (gridViewDS.Columns[i].ValueType == typeof(Image))
                {
                    ((DataGridViewImageColumn)gridViewDS.Columns[i]).ImageLayout = DataGridViewImageCellLayout.Zoom;
                    gridViewDS.Columns[i].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
                }
            }
            ApplyDataSetColumnHeaders();
        }

        private void ApplyDataSetColumnHeaders()
        {
            if (gridViewDS.Columns.Contains("Img"))
                gridViewDS.Columns["Img"].HeaderText = I18n.GetText("GridImage");
            if (gridViewDS.Columns.Contains("Name"))
                gridViewDS.Columns["Name"].HeaderText = I18n.GetText("GridName");
        }

        private void dataGridView3_SelectionChanged(object sender, EventArgs e)
        {
            if (!selectionMode)
                LoadSelectedImageToGrid();
        }


        private int GetgridViewTagsHash()
        {
            List<string> tags = new List<string>();
            for (int i = 0; i < gridViewTags.RowCount; i++)
            {
                tags.Add((string)gridViewTags["ImageTags", i].Value);
            }
            return string.Join("|", tags).GetHashCode();
        }

        private void dataGridViewTags_CellMouseEnter(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex != -1 && e.RowIndex != -1)
            {
                if (gridViewTags.Columns["ImageName"].Visible)
                {
                    if (e.RowIndex != previewRowIndex)
                    {

                        //var dataItem = Program.DataManager.DataSet[(string)gridViewTags["Image", e.RowIndex].Value];
                        //var dataItem = (DataItem)gridViewTags["ImageTags", e.RowIndex].Tag;
                        var dataItem = (DataItem)((MultiSelectDataRow)((MultiSelectDataTable)gridViewTags.DataSource).Rows[e.RowIndex]).ExtendedProperties["DataItem"];
                        previewPicBox.Size = new Size(Program.Settings.PreviewSize, Program.Settings.PreviewSize);
                        previewPicBox.Image = dataItem.Img;
                        previewPicBox.SizeMode = PictureBoxSizeMode.AutoSize;
                        previewPicBox.Location = new Point(splitContainer1.Panel2.Location.X + splitContainer2.Panel2.Location.X, PointToClient(Cursor.Position).Y);

                        if (!this.Controls.ContainsKey("previewPicBox"))
                        {
                            this.Controls.Add(previewPicBox);
                        }
                        previewPicBox.BringToFront();
                        previewRowIndex = e.RowIndex;
                    }
                }
                else
                {
                    if (this.Controls.ContainsKey("previewPicBox"))
                    {
                        this.Controls.RemoveByKey("previewPicBox");
                        previewRowIndex = -1;
                    }
                }
            }
        }

        private void dataGridViewTags_CellMouseLeave(object sender, DataGridViewCellEventArgs e)
        {
            if (this.Controls.ContainsKey("previewPicBox"))
            {
                this.Controls.RemoveByKey("previewPicBox");
                previewRowIndex = -1;
            }
        }

        private void toolStripButton18_Click(object sender, EventArgs e)
        {
            switch (filterAnd)
            {
                case FilterType.Not:
                    filterAnd = FilterType.Or;
                    BtnTagMultiModeSwitch.Image = Properties.Resources.ORIcon;
                    break;
                case FilterType.Or:
                    filterAnd = FilterType.Xor;
                    BtnTagMultiModeSwitch.Image = Properties.Resources.XORIcon;
                    break;
                case FilterType.Xor:
                    filterAnd = FilterType.And;
                    BtnTagMultiModeSwitch.Image = Properties.Resources.ANDIcon;
                    break;
                case FilterType.And:
                    filterAnd = FilterType.Not;
                    BtnTagMultiModeSwitch.Image = Properties.Resources.NOTIcon;
                    break;
                default:
                    throw new ArgumentException($"Invalid filter type: {filterAnd}");
            }
            SetFilter();
        }

        private async void gridViewTags_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (gridViewTags.Columns["ImageTags"].Index == e.ColumnIndex && e.RowIndex != -1)
            {
                string editedValue = (string)gridViewTags[e.ColumnIndex, e.RowIndex].Value;
                if (string.IsNullOrEmpty(editedValue))
                    return;
                if (gridViewDS.SelectedRows.Count == 1)
                {
                    for (int i = 0; i < gridViewTags.RowCount; i++)
                    {
                        if (i != e.RowIndex && (string)gridViewTags[e.ColumnIndex, i].Value == editedValue)
                        {
                            this.BeginInvoke(new MethodInvoker(() =>
                            {
                                gridViewTags.Rows.RemoveAt(e.RowIndex);
                            }));

                        }
                    }
                }
                else if (gridViewDS.SelectedRows.Count > 1)
                {
                    if (string.IsNullOrEmpty((string)gridViewTags["Image", e.RowIndex].Value))
                    {
                        MessageBox.Show(I18n.GetText("TipImageNameMustFilled"));
                        this.BeginInvoke(new MethodInvoker(() =>
                        {
                            gridViewTags.Rows.RemoveAt(e.RowIndex);
                        }));
                    }
                    else
                    {
                        //gridViewTags["Image", e.RowIndex].Tag = gridViewTags["ImageTags", e.RowIndex].Value;
                        //gridViewTags["Name", e.RowIndex].Tag = gridViewTags["ImageTags", e.RowIndex].Value;
                    }
                }
            }
            if (isTranslate && gridViewTags.DataSource is EditableTagList editableTags)
            {
                try
                {
                    await editableTags.TranslateAllAsync();
                }
                catch (Exception ex)
                {
                    // Fires after every cell edit (async void): a translation
                    // backend failure must not escape as unhandled.
                    Trace.WriteLine($"TranslateAllAsync failed: {ex}");
                    SetStatus(ex.Message);
                }
            }
        }

        private void toolStripButton19_Click(object sender, EventArgs e)
        {
            AddSelectedAllTagsToImageTags();
        }

        private void AddTagSingleSelectedMode(string tag)
        {
            if (gridViewDS.SelectedRows.Count != 1)
            {
                SetStatus(I18n.GetText("TipSelectedImgMustEqual1"));
                return;
            }

            for (int i = 0; i < gridViewTags.RowCount; i++)
            {
                if ((string)gridViewTags["ImageTags", i].Value == tag)
                {
                    return;
                }
            }
            ((EditableTagList)gridViewTags.DataSource).AddTag(tag, true);
        }

        private async void AddTagMultiselectedMode(string tag, bool skipExist, AddingType addType, int pos = -1)
        {
            if (gridViewDS.SelectedRows.Count < 2)
            {
                SetStatus(I18n.GetText("TipSelectedImgMustGreated1"));
                return;
            }
            ((MultiSelectDataTable)gridViewTags.DataSource).AddTag(tag, skipExist, addType, pos);
        }

        private void RemoveTagFromImageTags(string tag)
        {
            if (gridViewDS.SelectedRows.Count == 0)
            {
                SetStatus(I18n.GetText("TipSelectedImgMustGreated0"));
                return;
            }

            for (int i = gridViewTags.RowCount - 1; i >= 0; i--)
            {
                if (gridViewDS.SelectedRows.Count == 1)
                {
                    if ((string)gridViewTags["ImageTags", i].Value == tag)
                    {
                        gridViewTags.Rows.RemoveAt(i);
                    }
                }
                else
                {
                    var rowTag = (string)((MultiSelectDataRow)((MultiSelectDataTable)gridViewTags.DataSource).Rows[i]).ExtendedProperties["TextTag"];
                    if (rowTag == tag)
                    {
                        gridViewTags.Rows.RemoveAt(i);
                    }
                }
            }
        }

        private List<string> GetSelectedTagsInAllTags()
        {
            List<string> selectedTags = new List<string>();
            for (int i = 0; i < gridViewAllTags.SelectedCells.Count; i++)
            {
                var row = gridViewAllTags.SelectedCells[i].RowIndex;
                var tag = (string)gridViewAllTags.Rows[row].Cells["TagsColumn"].Value;
                if (!selectedTags.Contains(tag))
                    selectedTags.Add(tag);
            }
            return selectedTags;
        }

        private async void AddSelectedAllTagsToImageTags()
        {
            if (gridViewAllTags.SelectedCells.Count == 0 || gridViewDS.SelectedRows.Count == 0)
            {
                SetStatus(I18n.GetText("TipImgOrTagNotSelect"));
                return;
            }
            foreach (var item in GetSelectedTagsInAllTags())
            {
                if (gridViewDS.SelectedRows.Count == 1)
                    AddTagSingleSelectedMode(item);
                else
                    AddTagMultiselectedMode(item, true, AddingType.Down);
            }
            if (isTranslate)
                await FillTranslation(gridViewTags);
        }

        private void RemoveSelectedAllTagsToImageTags()
        {
            if (gridViewAllTags.SelectedCells.Count == 0 || gridViewDS.SelectedRows.Count == 0)
            {
                SetStatus(I18n.GetText("TipImgOrTagNotSelect"));
                return;
            }
            foreach (var item in GetSelectedTagsInAllTags())
            {
                RemoveTagFromImageTags(item);
            }
        }

        private void toolStripButton20_Click(object sender, EventArgs e)
        {
            RemoveSelectedAllTagsToImageTags();
        }

        private void toolStripButton21_Click(object sender, EventArgs e)
        {
            AddTagToAll(true);
        }

        private void toolStripButton22_Click(object sender, EventArgs e)
        {
            RemoveTagFromAll(true);
        }

        private void gridViewDS_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.ColumnIndex != -1)
            {
                if (Enum.IsDefined(typeof(DatasetManager.OrderType), gridViewDS.Columns[e.ColumnIndex].Name))
                {
                    isLoading = true;
                    gridViewDS.DataSource = Program.DataManager.GetDataSourceWithLastFilter((DatasetManager.OrderType)Enum.Parse(typeof(DatasetManager.OrderType), gridViewDS.Columns[e.ColumnIndex].Name));
                    isLoading = false;
                }
            }
        }

        private void toolStripButton23_Click(object sender, EventArgs e)
        {
            string searchedTag;
            if (gridViewDS.SelectedRows.Count == 1)
            {
                searchedTag = (string)gridViewTags["ImageTags", gridViewTags.CurrentCell.RowIndex].Value;
            }
            else if (gridViewDS.SelectedRows.Count > 1)
            {
                searchedTag = (string)gridViewTags.Rows[gridViewTags.CurrentCell.RowIndex].Tag;
            }
            else
                return;
            for (int i = 0; i < gridViewAllTags.RowCount; i++)
            {
                if (((string)gridViewAllTags[0, i].Value) == searchedTag)
                {
                    gridViewAllTags.ClearSelection();
                    gridViewAllTags.Rows[i].Selected = true;
                    if (i < gridViewAllTags.FirstDisplayedScrollingRowIndex || i > gridViewAllTags.FirstDisplayedScrollingRowIndex + gridViewAllTags.DisplayedRowCount(false))
                    {
                        gridViewAllTags.FirstDisplayedScrollingRowIndex = i;
                    }
                }
            }
        }



        private void gridViewTags_KeyPress(object sender, KeyPressEventArgs e)
        {

        }

        private async void gridViewDS_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                DeleteImage();
            }
            if (e.Control && e.KeyCode == Keys.V)
            {
                await PasteTagsFromClipboard();
                e.SuppressKeyPress = true;
            }
        }

        private void DeleteImage()
        {
            if (gridViewDS.SelectedRows.Count < 1)
                return;
            if (MessageBox.Show(I18n.GetText("TipDeleteFile"), I18n.GetText("LabelDeleteFile"),
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            List<string> pathsToDelete = gridViewDS.SelectedRows
                .Cast<DataGridViewRow>()
                .Select(row => (string)row.Cells["ImageFilePath"].Value)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (pathsToDelete.Count == 0)
                return;

            List<DataItem> list = gridViewDS.DataSource as List<DataItem>;
            if (list == null)
                return;

            int scroll = gridViewDS.FirstDisplayedScrollingRowIndex;
            int select = gridViewDS.SelectedRows.Cast<DataGridViewRow>().Min(row => row.Index);
            HashSet<string> deletedPathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            selectionMode = true;
            gridViewDS.SuspendLayout();
            gridViewTags.DataSource = null;
            try
            {
                List<string> deletedPaths = new List<string>();
                List<string> failedPaths = new List<string>();
                foreach (string file in pathsToDelete)
                {
                    string tagFile = Path.Combine(Path.GetDirectoryName(file) ?? string.Empty, Path.GetFileNameWithoutExtension(file) + ".txt");
                    try
                    {
                        if (File.Exists(file))
                            File.Delete(file);
                        if (File.Exists(tagFile))
                            File.Delete(tagFile);
                        // Only mark as removed after a successful delete so the
                        // dataset stays consistent with what is actually on disk.
                        deletedPaths.Add(file);
                        deletedPathSet.Add(file);
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Failed to delete '{file}': {ex}");
                        failedPaths.Add(file);
                    }
                }

                if (deletedPaths.Count > 0)
                    Program.DataManager.RemoveMany(deletedPaths);

                if (failedPaths.Count > 0)
                {
                    MessageBox.Show(
                        this,
                        string.Format(I18n.GetText("TipDeleteFilesFailed"), failedPaths.Count)
                            + Environment.NewLine
                            + string.Join(Environment.NewLine, failedPaths.Take(10)),
                        I18n.GetText("LabelDeleteFile"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (deletedPathSet.Contains(list[i].ImageFilePath))
                        list.RemoveAt(i);
                }

                // The grid is bound to a plain List<DataItem>, which does not raise
                // change notifications. Control.Refresh() only repaints and leaves
                // the grid's cached row count out of sync with the shortened list,
                // producing stale rows with null cells (IndexOutOfRange on paint and
                // a null-key crash in LoadSelectedImageToGrid). CurrencyManager.Refresh()
                // forces the binding to re-read the list so the row count matches.
                if (gridViewDS.DataSource != null
                    && gridViewDS.BindingContext[gridViewDS.DataSource] is CurrencyManager cm)
                {
                    cm.Refresh();
                }
                gridViewDS.Refresh();
                if (gridViewDS.RowCount > 0)
                {
                    if (scroll >= 0)
                        gridViewDS.FirstDisplayedScrollingRowIndex = Math.Min(scroll, gridViewDS.RowCount - 1);
                    if (select >= gridViewDS.RowCount)
                        select = gridViewDS.RowCount - 1;
                    gridViewDS.ClearSelection();
                    gridViewDS.Rows[select].Selected = true;
                }
            }
            finally
            {
                gridViewDS.ResumeLayout();
                selectionMode = false;
            }

            SetDSCountStatus(string.Format(I18n.GetText("LabelShownDsImages"), gridViewDS.RowCount, Program.DataManager.DataSet.Count));
            if (gridViewDS.SelectedRows.Count > 0)
                LoadSelectedImageToGrid();
        }

        private void gridViewDS_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && gridViewDS.SelectedRows.Count > 0)
            {
                var file = (string)gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value;
                ShowPreview(file, true);
            }
        }

        private void toolStripMenuItem1_Click(object sender, EventArgs e)
        {
            if (gridViewDS.SelectedRows.Count > 0)
            {
                var file = (string)gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value;
                ExplorerFile(file);
            }
        }

        private void ContextMenuStrip1_Opening(object sender, CancelEventArgs e)
        {
            if (gridViewDS.SelectedRows.Count == 0)
                return;

            string file = (string)gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value;
            bool isVideo = VideoProcessingService.IsVideoFile(file);
            cropImageToolStripMenuItem.Visible = !isVideo;
            removeBackgroundToolStripMenuItem.Visible = !isVideo;
            if (menuContextDSRetagOnnx != null)
                menuContextDSRetagOnnx.Visible = !isVideo;
            if (menuContextDSRetagLlm != null)
                menuContextDSRetagLlm.Visible = !isVideo;
            if (menuContextDSVideoTools != null)
                menuContextDSVideoTools.Visible = isVideo;
        }

        private void gridViewDS_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex != -1 && e.RowIndex < gridViewDS.Rows.Count && e.Button == MouseButtons.Right)
            {
                gridViewDS.ClearSelection();
                gridViewDS.Rows[e.RowIndex].Selected = true;
                contextMenuStrip1.Show(MousePosition);
            }
            else if (e.RowIndex == -1 && gridViewDS.Rows.Count > 0 && e.Button == MouseButtons.Right)
            {
                contextMenuImageGridHeader.Items.Clear();
                for (int i = 0; i < gridViewDS.ColumnCount; i++)
                {
                    ToolStripMenuItem tsi = new ToolStripMenuItem();
                    tsi.Name = gridViewDS.Columns[i].Name;
                    tsi.Text = gridViewDS.Columns[i].HeaderText;
                    tsi.Checked = gridViewDS.Columns[i].Visible;
                    contextMenuImageGridHeader.Items.Add(tsi);
                }
                contextMenuImageGridHeader.Show(MousePosition);
            }
        }

        [DllImport("shell32.dll", ExactSpelling = true)]
        private static extern void ILFree(IntPtr pidlList);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr ILCreateFromPathW(string pszPath);

        [DllImport("shell32.dll", ExactSpelling = true)]
        private static extern int SHOpenFolderAndSelectItems(IntPtr pidlList, uint cild, IntPtr children, uint dwFlags);

        public static void ExplorerFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                IntPtr pidlList = ILCreateFromPathW(filePath);
                if (pidlList != IntPtr.Zero)
                {
                    try
                    {
                        SHOpenFolderAndSelectItems(pidlList, 0, IntPtr.Zero, 0);
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Failed to open Explorer for '{filePath}': {ex}");
                    }
                    finally
                    {
                        ILFree(pidlList);
                    }
                }
                return;
            }

            if (Directory.Exists(filePath))
            {
                Process.Start(@"explorer.exe", "/select,\"" + filePath + "\"");
                return;
            }
            var dir = Path.GetDirectoryName(filePath);
            if (Directory.Exists(dir))
            {
                Process.Start(@"explorer.exe", "\"" + dir + "\"");
            }
        }

        private void toolStripMenuItem2_Click(object sender, EventArgs e)
        {
            DeleteImage();
        }

        private void gridView_Enter(object sender, EventArgs e)
        {
            if (sender is DataGridView grid)
                grid.BorderStyle = BorderStyle.FixedSingle;
        }

        private void gridView_Leave(object sender, EventArgs e)
        {
            if (sender is DataGridView grid)
                grid.BorderStyle = BorderStyle.Fixed3D;
        }

        private void ShowAllTagsFilter(bool show)
        {
            if (!show)
                toolStripTextBox1.TextChanged -= TextBox1_TextChanged;
            toolStripTextBox1.Clear();
            toolStripTextBox1.Visible = show;
            toolStripButton1.Visible = show;
            if (show)
            {
                toolStripTextBox1.Focus();
                toolStripTextBox1.TextChanged += TextBox1_TextChanged;
            }
            else
            {
                gridViewAllTags.Focus();
            }
        }

        private void TextBox1_TextChanged(object sender, EventArgs e)
        {
            if (toolStripTextBox1.Text.Length > 0)
            {
                isLoading = true;
                int index = Program.DataManager.AllTags.FindTagStartWith(toolStripTextBox1.Text);
                if (index != -1)
                {
                    //gridViewAllTags.ClearSelection();
                    //gridViewAllTags.Rows[index].Selected = true;
                    gridViewAllTags.CurrentCell = gridViewAllTags.Rows[index].Cells[0];
                    if (index < gridViewAllTags.FirstDisplayedScrollingRowIndex || index > gridViewAllTags.FirstDisplayedScrollingRowIndex + gridViewAllTags.DisplayedRowCount(false))
                    {
                        gridViewAllTags.FirstDisplayedScrollingRowIndex = index;
                    }
                }
                else
                {
                    toolStripTextBox1.Text = toolStripTextBox1.Text.Substring(0, toolStripTextBox1.Text.Length - 1);
                    toolStripTextBox1.SelectionStart = toolStripTextBox1.TextLength;
                }
                isLoading = false;
            }
        }

        private void gridViewAllTags_KeyPress(object sender, KeyPressEventArgs e)
        {
            ShowAllTagsFilter(true);
            toolStripTextBox1.Text = e.KeyChar.ToString();
            toolStripTextBox1.SelectionStart = 1;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            ShowAllTagsFilter(false);
        }

        private void gridViewAllTags_SelectionChanged(object sender, EventArgs e)
        {
            if (!isLoading)
                ShowAllTagsFilter(false);
        }

        private void textBox1_KeyDown(object sender, KeyEventArgs e)
        {
            int pos = -1;
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                ShowAllTagsFilter(false);
                if (e.KeyCode == Keys.Down)
                    pos = 1;
                int index = gridViewAllTags.CurrentCell.RowIndex;
                gridViewAllTags.CurrentCell = gridViewAllTags.Rows[index + pos].Cells[0];
            }
        }

        private void toolStripButton24_Click(object sender, EventArgs e)
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }
            if (allTagsFilter == null || allTagsFilter.IsDisposed)
            {
                allTagsFilter = new Form_filter();
            }
            if (allTagsFilter.ShowDialog() != DialogResult.OK)
                return;
            if (isAllTags)
            {
                ((BindingSource)gridViewAllTags.DataSource).Filter = allTagsFilter.textBox1.Text;
            }
        }

        private void toolStripButton25_Click(object sender, EventArgs e)
        {
            ((BindingSource)gridViewAllTags.DataSource).RemoveFilter();
        }

        private void promptSortBtn_Click(object sender, EventArgs e)
        {
            SortPrompt();
        }

        private void SortPrompt()
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }
            var fixedLengthIndex = toolStrippromptFixedLengthComboBox.SelectedIndex;
            if (fixedLengthIndex == -1)
                return;
            if (GetTagsDataSourceType() == DataSourceType.Single)
            {
                EditableTagList eTagList = (EditableTagList)gridViewTags.DataSource;
                eTagList.Sort(fixedLengthIndex);
            }
        }

        public void switchLanguage()
        {
            I18n.Initialize(Program.Settings.Language);
            fileToolStripMenuItem.Text = I18n.GetText("MenuLabelFile");
            viewToolStripMenuItem.Text = I18n.GetText("MenuLabelView");
            toolStripLabelDataSet.Text = I18n.GetText("UILabelDataSet");
            toolStripLabelAllTags.Text = I18n.GetText("UILabelAllTags");
            toolStripLabelImageTags.Text = I18n.GetText("UILabelImageTags");
            toolStripPromptFixTipLabel.Text = I18n.GetText("UILabelFixPromptLength");
            openFolderToolStripMenuItem.Text = I18n.GetText("MenuItemLoadFolder");
            loadFolderWithAdditionalSettingsToolStripMenuItem.Text = I18n.GetText("MenuItemLoadFolderWithSettings");
            saveAllChangesToolStripMenuItem.Text = I18n.GetText("MenuItemSaveChanges");
            MenuShowPreview.Text = I18n.GetText("MenuItemShowPreview");
            MenuItemTranslateTags.Text = I18n.GetText("MenuItemTranslateTags");
            MenuHideAllTags.Text = I18n.GetText("MenuHideAllTags");
            MenuHideTags.Text = I18n.GetText("MenuHideTags");
            MenuHideDataset.Text = I18n.GetText("MenuHideDataset");
            MenuLanguage.Text = I18n.GetText("MenuMenuLanguage");
            MenuSetting.Text = I18n.GetText("MenuLabelOptions");
            settingsToolStripMenuItem.Text = I18n.GetText("MenuSettings");
            autoTaggerSettingsToolStripMenuItem.Text = I18n.GetText("MenuAutoTaggerSettings");
            toolsToolStripMenuItem.Text = I18n.GetText("MenuTools");
            if (menuAiServerSet != null)
                menuAiServerSet.Text = I18n.GetText("MenuAiServerSet");
            if (menuLlmTagger != null)
                menuLlmTagger.Text = I18n.GetText("MenuLlmTagger");
            if (menuVideoConvert != null)
                menuVideoConvert.Text = I18n.GetText("MenuVideoConvert");
            if (menuVideoExtract != null)
                menuVideoExtract.Text = I18n.GetText("MenuVideoExtract");
            if (menuOnnxTagger != null)
                menuOnnxTagger.Text = I18n.GetText("MenuOnnxTagger");
            if (menuTestModule != null)
                menuTestModule.Text = I18n.GetText("MenuTestModule");
            replaceTransparentBackgroundToolStripMenuItem.Text = I18n.GetText("MenuReplaceTranspColor");
            generateTagsWithAutoTaggerForAllImagesToolStripMenuItem.Text = I18n.GetText("MenuGenTagsForAllImages");
            MenuOpenAiGenTagsForAllImages.Text = I18n.GetText("MenuOpenAiGenTagsForAllImages");
            cropImagesWithMoondream2ToolStripMenuItem.Text = I18n.GetText("MenuToolsAutoCropping");
            backgroundRemovalWithRMBG20ToolStripMenuItem.Text = I18n.GetText("MenuToolsBGRemoval");
            removeBackgroundToolStripMenuItem.Text = I18n.GetText("MenuContextDSRemoveBG");
            toolStripMenuItem1.Text = I18n.GetText("MenuContextDSOpenFolder");
            toolStripMenuItem2.Text = I18n.GetText("MenuContextDSDeleteImage");
            cropImageToolStripMenuItem.Text = I18n.GetText("MenuContextDSCropImage");
            if (menuContextDSVideoTools != null)
                menuContextDSVideoTools.Text = I18n.GetText("MenuContextDSVideoTools");
            if (menuContextDSRetagOnnx != null)
                menuContextDSRetagOnnx.Text = I18n.GetText("MenuContextDSRetagOnnx");
            if (menuContextDSRetagLlm != null)
                menuContextDSRetagLlm.Text = I18n.GetText("MenuContextDSRetagLlm");
            UpdateTagContextMenuText();

            BtnTagAddToAll.Text = I18n.GetText("BtnTagAddToAll");
            BtnTagAdd.Text = I18n.GetText("BtnTagAdd");
            BtnTagUndo.Text = I18n.GetText("BtnTagUndo");
            BtnTagRedo.Text = I18n.GetText("BtnTagRedo");
            BtnTagDelete.Text = I18n.GetText("BtnTagDelete");
            BtnTagCopy.Text = I18n.GetText("BtnTagCopy");
            BtnTagPaste.Text = I18n.GetText("BtnTagPaste");
            BtnTagSetToAll.Text = I18n.GetText("BtnTagSetToAll");
            BtnTagPasteFromClipBoard.Text = I18n.GetText("BtnTagPasteFromClipBoard");
            BtnTagShow.Text = I18n.GetText("BtnTagShow");
            BtnTagUp.Text = I18n.GetText("BtnTagUp");
            BtnTagDown.Text = I18n.GetText("BtnTagDown");
            BtnTagFindInAll.Text = I18n.GetText("BtnTagFindInAll");
            toolStripSplitButton1.Text = I18n.GetText("BtnAutoGenerateTagsRoot");
            btnAutoGetTagsDefSet.Text = I18n.GetText("BtnAutoGetTagsDefSet");
            btnOpenAiAutoGetTagsDefSet.Text = I18n.GetText("BtnOpenAiAutoGetTagsDefSet");
            btnAutoGetTagsOpenSet.Text = I18n.GetText("BtnAutoGetTagsOpenSet");
            btnOpenAiAutoGetTagsOpenSet.Text = I18n.GetText("BtnOpenAiAutoGetTagsOpenSet");
            btnAutoAddSelToImageTags.Text = I18n.GetText("BtnAutoAddSelToImageTags");
            BtnMenuSorting.Text = I18n.GetText("BtnMenuSorting");
            BtnMenuSortNameAsc.Text = I18n.GetText("BtnMenuSortNameAsc");
            BtnMenuSortCountAsc.Text = I18n.GetText("BtnMenuSortCountAsc");
            BtnMenuSortCountDesc.Text = I18n.GetText("BtnMenuSortCountDesc");
            BtnTagImageChecker.Text = I18n.GetText("BtnTagImageChecker");

            BtnTagSwitch.Text = I18n.GetText("BtnTagSwitch");
            BtnTagAddToAll.Text = I18n.GetText("BtnTagAddToAll");
            BtnTagDeleteForAll.Text = I18n.GetText("BtnTagDeleteForAll");
            BtnTagReplace.Text = I18n.GetText("BtnTagReplace");
            BtnTagAddToSelected.Text = I18n.GetText("BtnTagAddToSelected");
            BtnTagDeleteForSelected.Text = I18n.GetText("BtnTagDeleteForSelected");
            BtnTagAddToFiltered.Text = I18n.GetText("BtnTagAddToFiltered");
            BtnTagDeleteForFiltered.Text = I18n.GetText("BtnTagDeleteForFiltered");
            BtnTagMultiModeSwitch.Text = I18n.GetText("BtnTagMultiModeSwitch");
            BtnImageFilter.Text = I18n.GetText("BtnImageFilter");
            BtnImageExitFilter.Text = I18n.GetText("BtnImageExitFilter");
            BtnTagFilter.Text = I18n.GetText("BtnTagFilter");
            BtnTagExitFilter.Text = I18n.GetText("BtnTagExitFilter");
            MenuShowTagCount.Text = I18n.GetText("MenuShowCount");
            BtnMenuGenTagsWithCurrentSettings.Text = I18n.GetText("BtnMenuGenTagsWithCurrentSettings");
            BtnMenuOpenAiGenTagsWithCurrentSettings.Text = I18n.GetText("BtnMenuOpenAiGenTagsWithCurrentSettings");
            BtnMenuGenTagsWithSetWindow.Text = I18n.GetText("BtnMenuGenTagsWithSetWindow");
            BtnMenuOpenAiGenTagsWithSetWindow.Text = I18n.GetText("BtnMenuOpenAiGenTagsWithSetWindow");
            toolStripPromptSortBtn.Text = I18n.GetText("toolStripPromptSortBtn");
            toolStripLabelWeight.Text = I18n.GetText("UILabelWeight");
            tabAllTags.Text = I18n.GetText("UITabAllTags");
            tabAutoTags.Text = I18n.GetText("UITabAutoTags");
            BtnDSChangeSelection.Text = I18n.GetText("BtnDSChangeSelection");
            ApplyDataSetColumnHeaders();


            foreach (ToolStripMenuItem item in MenuLanguage.DropDownItems)
            {
                if (item.Name == "btn_" + Program.Settings.Language)
                {
                    item.Checked = true;
                }
                else
                {
                    item.Checked = false;
                }
            }
        }

        private void LanguageXXBtn_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem item = (ToolStripMenuItem)sender;
            if (item.Checked)
                return;
            string lang = item.Name.Substring(4);
            Program.Settings.Language = lang;
            Program.Settings.SaveSettings();
            switchLanguage();
        }

        private void MenuShowTagCount_Click(object sender, EventArgs e)
        {
            showCount = !showCount;
            MenuShowTagCount.Checked = showCount;
            if (showCount)
            {
                gridViewAllTags.Columns["CountColumn"].Visible = true;
            }
            else
            {
                gridViewAllTags.Columns["CountColumn"].Visible = false;
            }
        }

        private async void BtnTagRedo_Click(object sender, EventArgs e)
        {
            await ApplyTagHistoryActionAsync(tags => tags.NextState());
        }

        private async Task ApplyTagHistoryActionAsync(Action<EditableTagList> historyAction)
        {
            if (GetTagsDataSourceType() != DataSourceType.Single)
            {
                MessageBox.Show(I18n.GetText("TipStateMultiselectNotSupported"));
                return;
            }

            if (!(gridViewTags.DataSource is EditableTagList editableTags))
                return;

            int preferredRow = gridViewTags.CurrentCell?.RowIndex ?? -1;
            historyAction(editableTags);
            RefreshTagGridAfterHistory(preferredRow);

            if (isTranslate)
            {
                await FillTranslation(gridViewTags);
                RefreshTagGridAfterHistory(preferredRow);
            }
        }

        private void RefreshTagGridAfterHistory(int preferredRow)
        {
            if (gridViewTags.DataSource != null)
            {
                if (BindingContext[gridViewTags.DataSource] is CurrencyManager currencyManager)
                    currencyManager.Refresh();
            }

            gridViewTags.Refresh();

            if (gridViewTags.RowCount == 0 || gridViewTags.ColumnCount == 0)
                return;

            int rowIndex = preferredRow < 0
                ? 0
                : Math.Min(preferredRow, gridViewTags.RowCount - 1);
            string columnName = gridViewTags.Columns.Contains("ImageTags")
                ? "ImageTags"
                : gridViewTags.Columns[0].Name;

            gridViewTags.ClearSelection();
            gridViewTags.CurrentCell = gridViewTags[columnName, rowIndex];
            gridViewTags[columnName, rowIndex].Selected = true;
        }

        private void gridViewTags_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex != -1 && e.RowIndex != -1)
            {
                if (gridViewTags[e.ColumnIndex, e.RowIndex].Value == DBNull.Value)
                    gridViewTags[e.ColumnIndex, e.RowIndex].Value = string.Empty;
            }
        }

        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            HidePreview();
            Form_settings settings = new Form_settings();
            if (settings.ShowDialog() == DialogResult.OK)
            {
                SetStatus(I18n.GetText("TipSettingsSaved"));
            }
            settings.Close();
            switchLanguage();
            if (isShowPreview)
            {
                if (gridViewDS.SelectedRows.Count == 1)
                {
                    ShowPreview((string)gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value);
                }
                else
                {
                    HidePreview();
                }
            }
        }

        private async void replaceTransparentBackgroundToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (gridViewDS.SelectedRows.Count == 0)
                return;
            using Form_backgroundReplace backgroundReplace = new Form_backgroundReplace();
            if (backgroundReplace.ShowDialog() != DialogResult.OK)
                return;
            LockEdit(true);
            try
            {
                SetStatus(I18n.GetText("InProgress"));
                bool randomColor = backgroundReplace.radioButton2.Checked;
                bool randomSelectedColor = backgroundReplace.radioButton3.Checked;
                Color replColor = backgroundReplace.pictureBox1.BackColor;
                // Snapshot the candidate colors on the UI thread: the worker loop
                // below must not read controls (illegal cross-thread access).
                List<Color> selectedColors = new List<Color>();
                foreach (ListViewItem lvItem in backgroundReplace.listView1.Items)
                    selectedColors.Add(lvItem.BackColor);
                List<DataItem> selectedTagsList = new List<DataItem>();
                Random r = new Random();
                for (int i = 0; i < gridViewDS.SelectedRows.Count; i++)
                {
                    if (Program.DataManager.DataSet.TryGetValue((string)gridViewDS.SelectedRows[i].Cells["ImageFilePath"].Value, out DataItem selectedItem))
                        selectedTagsList.Add(selectedItem);
                }
                // Old thumbnails are collected and disposed on the UI thread after the
                // background work: the grid may still paint them while the loop runs.
                List<Image> replacedThumbs = new List<Image>();
                List<string> errors = new List<string>();
                await Task.Run(() =>
                {
                    foreach (var item in selectedTagsList)
                    {
                        try
                        {
                            System.Drawing.Imaging.ImageFormat format;
                            string tmpPath = item.ImageFilePath + ".tmp";
                            using (Bitmap bmp = (Bitmap)Bitmap.FromFile(item.ImageFilePath))
                            {
                                format = bmp.RawFormat;
                                if (randomColor)
                                {
                                    replColor = Color.FromArgb(r.Next(255), r.Next(255), r.Next(255));
                                }
                                else if (randomSelectedColor && selectedColors.Count > 0)
                                {
                                    replColor = selectedColors[r.Next(selectedColors.Count)];
                                }
                                using Bitmap bmpRes = Extensions.Transparent2Color(bmp, replColor);
                                bmpRes.Save(tmpPath, format);
                            }
                            // Source handle released above; swap the finished file in.
                            SafeFile.ReplaceOrMove(tmpPath, item.ImageFilePath, null);
                            if (item.Img != null)
                                replacedThumbs.Add(item.Img);
                            item.Img = Extensions.MakeThumb(item.ImageFilePath, Program.Settings.PreviewSize);
                        }
                        catch (Exception ex)
                        {
                            // One broken/locked image must not abort the batch (and,
                            // pre-fix, crash the app via this unprotected async void).
                            errors.Add($"{item.ImageFilePath}: {ex.Message}");
                        }
                    }
                });
                gridViewDS.Refresh();
                foreach (Image oldThumb in replacedThumbs)
                    oldThumb.Dispose();
                if (errors.Count > 0)
                    MessageBox.Show(string.Join("\n", errors));
                SetStatus(I18n.GetText("TipBackgrRepComplete"));
            }
            finally
            {
                LockEdit(false);
            }
        }

        #region HotkeysCode

        private void InitHotkeyCommands()
        {
            if (Program.Settings.Hotkeys.Commands == null)
                Program.Settings.Hotkeys.Commands = new Dictionary<string, Action>();
            var cmds = Program.Settings.Hotkeys.Commands;

            cmds["DatasetFocus"] = delegate () { DatasetFocus(); };
            cmds["TagsFocus"] = delegate () { TagsFocus(); };
            cmds["AllTagsFocus"] = delegate () { AllTagsFocus(); };
            cmds["AutoTagsFocus"] = delegate () { AutoTagsFocus(); };
            cmds["PreviewTabFocus"] = delegate () { PreviewTabFocus(); };

            cmds["MenuItemSaveChanges"] = delegate () { saveAllChangesToolStripMenuItem.PerformClick(); };
            cmds["MenuItemShowPreview"] = delegate () { MenuShowPreview.PerformClick(); };
            cmds["MenuHideAllTags"] = delegate () { MenuHideAllTags.PerformClick(); };
            cmds["MenuHideTags"] = delegate () { MenuHideTags.PerformClick(); };
            cmds["MenuHideDataset"] = delegate () { MenuHideDataset.PerformClick(); };


            cmds["BtnTagAdd"] = delegate () { BtnTagAdd.PerformClick(); TagsFocus(); };
            cmds["BtnTagDelete"] = delegate () { BtnTagDelete.PerformClick(); };
            cmds["BtnTagUndo"] = delegate () { BtnTagUndo.PerformClick(); };
            cmds["BtnTagRedo"] = delegate () { BtnTagRedo.PerformClick(); };
            cmds["BtnTagUp"] = delegate () { BtnTagUp.PerformClick(); };
            cmds["BtnTagDown"] = delegate () { BtnTagDown.PerformClick(); };
            cmds["BtnTagFindInAll"] = delegate () { BtnTagFindInAll.PerformClick(); };
            cmds["BtnTagAddToAll"] = delegate () { BtnTagAddToAll.PerformClick(); };
            cmds["BtnTagAddToSelected"] = delegate () { BtnTagAddToSelected.PerformClick(); };
            cmds["BtnTagAddToFiltered"] = delegate () { BtnTagAddToFiltered.PerformClick(); };
            cmds["BtnTagDeleteForAll"] = delegate () { BtnTagDeleteForAll.PerformClick(); };
            cmds["BtnTagDeleteForSelected"] = delegate () { BtnTagDeleteForSelected.PerformClick(); };
            cmds["BtnTagDeleteForFiltered"] = delegate () { BtnTagDeleteForFiltered.PerformClick(); };
            cmds["BtnTagReplace"] = delegate () { BtnTagReplace.PerformClick(); };
            cmds["BtnImageFilter"] = delegate () { BtnImageFilter.PerformClick(); };
            cmds["BtnImageExitFilter"] = delegate () { BtnImageExitFilter.PerformClick(); };
            cmds["BtnTagMultiModeSwitch"] = delegate () { BtnTagMultiModeSwitch.PerformClick(); };
            cmds["BtnTagFilter"] = delegate () { BtnTagFilter.PerformClick(); };
            cmds["BtnTagExitFilter"] = delegate () { BtnTagExitFilter.PerformClick(); };
            cmds["BtnMenuGenTagsWithCurrentSettings"] = delegate () { BtnMenuGenTagsWithCurrentSettings.PerformClick(); };
            cmds["BtnMenuGenTagsWithSetWindow"] = delegate () { BtnMenuGenTagsWithSetWindow.PerformClick(); };
            cmds["toolStripPromptSortBtn"] = delegate () { toolStripPromptSortBtn.PerformClick(); };
            cmds["BtnTagImageChecker"] = delegate () { BtnTagImageChecker.PerformClick(); };
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (gridViewTags.IsCurrentCellInEditMode && (keyData == Keys.Enter || keyData == Keys.Tab))
            {
                var rowIndex = gridViewTags.CurrentCell.RowIndex;
                gridViewTags.EndEdit();
                if (GetTagsDataSourceType() == DataSourceType.Single)
                {
                    var eTagList = ((EditableTagList)gridViewTags.DataSource);
                    eTagList.EndEdit(rowIndex);
                }
                else if (GetTagsDataSourceType() == DataSourceType.Multi)
                {
                    var mTagList = ((MultiSelectDataTable)gridViewTags.DataSource);
                    mTagList.EndEdit();
                }
                //((EditableTagList)gridViewTags.DataSource).
            }
            else if (keyData == (Keys.Control | Keys.Z) && CanHandleTagHistoryShortcut())
            {
                BtnTagUndo.PerformClick();
                return true;
            }
            else if (keyData == (Keys.Control | Keys.Y) && CanHandleTagHistoryShortcut())
            {
                BtnTagRedo.PerformClick();
                return true;
            }
            else if (gridViewTags.Focused && !gridViewTags.IsCurrentCellInEditMode && keyData == Keys.Enter)
            {
                gridViewTags.BeginEdit(true);
                return true;
            }
            var hotkey = Program.Settings.Hotkeys.Items.Find(a => a.FullKeyData == keyData);
            if (hotkey != null)
            {
                Program.Settings.Hotkeys.Commands[hotkey.Id]();
                return true;
            }
            else
                return base.ProcessCmdKey(ref msg, keyData);
        }

        private bool CanHandleTagHistoryShortcut()
        {
            if (gridViewTags.IsCurrentCellInEditMode)
                return false;

            if (ActiveControl is TextBoxBase || ActiveControl is ComboBox)
                return false;

            return true;
        }

        private void DatasetFocus()
        {
            gridViewDS.Focus();
        }

        private void TagsFocus()
        {
            gridViewTags.Focus();
        }

        private void AllTagsFocus()
        {
            tabControl1.SelectedIndex = 0;
            gridViewAllTags.Focus();
        }

        private void AutoTagsFocus()
        {
            tabControl1.SelectedIndex = 1;
            gridViewAutoTags.Focus();
        }
        private void PreviewTabFocus()
        {
            tabControl1.SelectedIndex = 2;
            gridViewDS.Focus();
        }

        private void HideShowAllTagsWindow()
        {
            if (splitContainer2.Panel1Collapsed)
                HideShowTagsWindow();
            splitContainer2.Panel2Collapsed = !splitContainer2.Panel2Collapsed;
            MenuHideAllTags.Checked = splitContainer2.Panel2Collapsed;
        }

        private void HideShowTagsWindow()
        {
            if (splitContainer2.Panel2Collapsed)
                HideShowAllTagsWindow();
            splitContainer2.Panel1Collapsed = !splitContainer2.Panel1Collapsed;
            MenuHideTags.Checked = splitContainer2.Panel1Collapsed;
        }

        private void HideShowDataset()
        {
            splitContainer1.Panel1Collapsed = !splitContainer1.Panel1Collapsed;
            MenuHideDataset.Checked = splitContainer1.Panel1Collapsed;
        }

        #endregion

        private void autoTaggerSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            EnsureOpenAiAutoTagConfigured(true);
        }

        internal void RefreshAfterCharacterTagAudit()
        {
            gridViewDS.Refresh();
            gridViewAllTags.Refresh();
            if (gridViewDS.SelectedRows.Count == 1)
            {
                string path = (string)gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value;
                if (Program.DataManager.DataSet.TryGetValue(path, out DataItem item))
                {
                    gridViewTags.DataSource = item.Tags;
                }
            }
            SetStatus(I18n.GetText("CharacterTagAuditSaved"));
        }

        private async void BtnAutoGetTagsDefSet_Click(object sender, EventArgs e)
        {
            await GetTagsWithAutoTagger(true);
        }

        private async Task GetTagsWithAutoTagger(bool defSettings)
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }
            if (gridViewDS.SelectedRows.Count == 0)
            {
                MessageBox.Show(I18n.GetText("TipSelectImage"));
                return;
            }
            if (!EnsureSelectedAutoTagProviderConfigured(!defSettings))
                return;

            SetStatus(I18n.GetText("InProgress"));
            tabAutoTags.Select();
            LockEdit(true);
            try
            {
                if (!Program.DataManager.DataSet.TryGetValue(
                        (string)gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value, out DataItem selectedImageData))
                    return;

                (List<AiApiClient.AutoTagItem> data, string errorMessage, bool canceled) taggerResult = (null, null, false);
                TaggerSettings settings = Program.Settings.OpenAiAutoTagger;
                taggerResult = await GenerateWithSelectedAutoTagProviderAsync(selectedImageData.ImageFilePath);
                if (!string.IsNullOrEmpty(taggerResult.errorMessage))
                    SetStatus(taggerResult.errorMessage);

                if (taggerResult.canceled)
                    return;
                if (taggerResult.data != null)
                {
                    if (settings.SetMode == NetworkResultSetMode.AllWithReplacement)
                        gridViewAutoTags.DataSource = taggerResult.data;
                    else if (settings.SetMode == NetworkResultSetMode.OnlyNewWithAddition)
                    {
                        foreach (var item in selectedImageData.Tags.TextTags)
                        {
                            taggerResult.data.RemoveAll(a => a.Tag == item);
                        }
                        gridViewAutoTags.DataSource = taggerResult.data;
                    }
                    else if (settings.SetMode == NetworkResultSetMode.SkipExistTagList)
                    {
                        if (selectedImageData.Tags.Count == 0)
                            gridViewAutoTags.DataSource = taggerResult.data;
                    }
                    SetStatus(I18n.GetText("TipProgressComplete"));
                }
            }
            finally
            {
                // Not in a finally before: any exception on the await path left the
                // whole UI permanently disabled.
                LockEdit(false);
            }
        }

        private async void generateTagsWithCurrentSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await GenerateTagsInTags(true, false);
        }

        internal async Task GenerateTagsInTags(bool defSettings, bool allTags)
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }
            if (!EnsureSelectedAutoTagProviderConfigured(!defSettings))
                return;

            TaggerSettings settings = Program.Settings.OpenAiAutoTagger;
            SetStatus(I18n.GetText("InProgress"));
            StringBuilder sbErrors = new StringBuilder();
            LockEdit(true);
            try
            {
            List<DataItem> selectedTagsList = new List<DataItem>();
            if (!allTags)
            {
                for (int i = 0; i < gridViewDS.SelectedRows.Count; i++)
                {
                    if (Program.DataManager.DataSet.TryGetValue((string)gridViewDS.SelectedRows[i].Cells["ImageFilePath"].Value, out DataItem selectedItem))
                        selectedTagsList.Add(selectedItem);
                }
            }
            else
            {
                foreach (var item in Program.DataManager.DataSet)
                {
                    selectedTagsList.Add(item.Value);
                }
            }
            int currentIndex = 0;
            foreach (var item in selectedTagsList)
            {
                (List<AiApiClient.AutoTagItem> data, string errorMessage, bool canceled) taggerResult = (null, null, false);
                taggerResult = await GenerateWithSelectedAutoTagProviderAsync(item.ImageFilePath);
                if (taggerResult.canceled)
                {
                    break;
                }
                if (!defSettings)
                    defSettings = true;
                if (!string.IsNullOrEmpty(taggerResult.errorMessage))
                    SetStatus(taggerResult.errorMessage);
                if (taggerResult.data != null)
                {
                    if (taggerResult.data.Count == 0)
                    {
                        SetStatus(string.Format(I18n.GetText("InProgressCount"), ++currentIndex, selectedTagsList.Count));
                        continue;
                    }
                    if (settings.SetMode == NetworkResultSetMode.AllWithReplacement)
                    {
                        item.Tags.Clear();
                        item.Tags.AddRange(taggerResult.data.Select(a => a.Tag), true);
                    }
                    else if (settings.SetMode == NetworkResultSetMode.OnlyNewWithAddition)
                    {
                        foreach (var aTag in taggerResult.data)
                        {
                            item.Tags.AddTag(aTag.Tag, true, AddingType.Down, 0);
                        }
                    }
                    else if (settings.SetMode == NetworkResultSetMode.SkipExistTagList)
                    {
                        if (item.Tags.Count == 0)
                        {
                            item.Tags.AddRange(taggerResult.data.Select(a => a.Tag), true);
                        }
                    }
                }
                else
                {
                    sbErrors.AppendLine(item.ImageFilePath);
                    //LockEdit(false);
                    //return;
                }
                SetStatus(string.Format(I18n.GetText("InProgressCount"), ++currentIndex, selectedTagsList.Count));
            }
            if (selectedTagsList.Count > 1)
            {
                LoadSelectedImageToGrid();
            }
            //if (gridViewAllTags.DataSource == null)
            //    BindTagList();
            }
            finally
            {
                LockEdit(false);
            }
            if (sbErrors.Length > 0)
            {
                sbErrors.Insert(0, "The following files were not processed (see ApiServer log):\n");
                MessageBox.Show(sbErrors.ToString());
            }
            SetStatus(I18n.GetText("TipProgressComplete"));
        }

        private async Task<bool> CropImages()
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return false;
            }
            using (Form_CropImage cropImageForm = new Form_CropImage(this))
            {
                if (gridViewDS.SelectedRows.Count > 1)
                    cropImageForm.radioButtonOnlySelected.Checked = true;
                if (cropImageForm.ShowDialog() != DialogResult.OK)
                    return false;
                bool allImages = cropImageForm.radioButtonAllImages.Checked;
                LockEdit(true);
                try
                {
                List<DataItem> selectedTagsList = new List<DataItem>();
                if (!allImages)
                {
                    for (int i = 0; i < gridViewDS.SelectedRows.Count; i++)
                    {
                        if (Program.DataManager.DataSet.TryGetValue((string)gridViewDS.SelectedRows[i].Cells["ImageFilePath"].Value, out DataItem selectedItem))
                            selectedTagsList.Add(selectedItem);
                        Program.DataManager.RemoveFromCache((string)gridViewDS.SelectedRows[i].Cells["ImageFilePath"].Value);
                    }
                }
                else
                {
                    foreach (var item in Program.DataManager.DataSet)
                    {
                        selectedTagsList.Add(item.Value);
                    }
                    Program.DataManager.ClearCache();
                }
                int index = 0;
                StringBuilder sbErrors = new StringBuilder();
                foreach (var item in selectedTagsList)
                {
                    try
                    {
                        var cropRect = await cropImageForm.CalcCropRectangle(item.ImageFilePath);
                        if (cropRect.Width <= 1 || cropRect.Height <= 1)
                        {
                            continue;
                        }
                        using (Bitmap target = new Bitmap(cropRect.Width, cropRect.Height))
                        {
                            using (Bitmap src = System.Drawing.Image.FromFile(item.ImageFilePath) as Bitmap)
                            {

                                using (Graphics g = Graphics.FromImage(target))
                                {
                                    g.DrawImage(src, new Rectangle(0, 0, target.Width, target.Height),
                                        cropRect,
                                        GraphicsUnit.Pixel);
                                }

                            }
                            // Encode to a temp file and swap: a failed encode
                            // (disk full, lock) must not destroy the source image.
                            string tmpPath = item.ImageFilePath + ".tmp";
                            target.Save(tmpPath);
                            SafeFile.ReplaceOrMove(tmpPath, item.ImageFilePath, null);
                        }
                    }
                    catch (Exception ex)
                    {
                        // One broken/locked image must not abort the whole batch.
                        sbErrors.AppendLine($"{item.ImageFilePath}: {ex.Message}");
                        continue;
                    }
                    try
                    {
                        Program.DataManager.DataSet[item.ImageFilePath].Img = Extensions.MakeThumb(item.ImageFilePath, Program.Settings.PreviewSize);
                    }
                    catch (Exception)
                    {
                        Program.DataManager.DataSet[item.ImageFilePath].Img = null;
                    }
                    SetStatus($"{++index} / {selectedTagsList.Count}");
                }
                gridViewDS.Refresh();
                if (sbErrors.Length > 0)
                    MessageBox.Show(sbErrors.ToString());
                //if (selectedTagsList.Count > 1)
                //{
                //    LoadSelectedImageToGrid();
                //}
                //if (gridViewAllTags.DataSource == null)
                //    BindTagList();
                return true;
                }
                finally
                {
                    LockEdit(false);
                }
            }
        }

        private async Task<bool> RemoveBackgrounds(bool onlySelected)
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return false;
            }
            using (Form_BGRemover bgRemoverForm = new Form_BGRemover(this))
            {
                if (onlySelected || gridViewDS.SelectedRows.Count > 1)
                    bgRemoverForm.radioButtonOnlySelected.Checked = true;
                if (bgRemoverForm.ShowDialog() != DialogResult.OK)
                    return false;
                string selectedModel = bgRemoverForm.GetSelectedModel();
                if (string.IsNullOrEmpty(selectedModel))
                    return false;
                bool allImages = bgRemoverForm.radioButtonAllImages.Checked;
                LockEdit(true);
                StringBuilder sbErrors = new StringBuilder();
                try
                {
                List<DataItem> selectedTagsList = new List<DataItem>();
                if (!allImages)
                {
                    for (int i = 0; i < gridViewDS.SelectedRows.Count; i++)
                    {
                        if (Program.DataManager.DataSet.TryGetValue((string)gridViewDS.SelectedRows[i].Cells["ImageFilePath"].Value, out DataItem selectedItem))
                            selectedTagsList.Add(selectedItem);
                        Program.DataManager.RemoveFromCache((string)gridViewDS.SelectedRows[i].Cells["ImageFilePath"].Value);
                    }
                }
                else
                {
                    foreach (var item in Program.DataManager.DataSet)
                    {
                        selectedTagsList.Add(item.Value);
                    }
                    Program.DataManager.ClearCache();
                }
                // Output mode chosen once for the whole batch.
                bool replace = bgRemoverForm.ReplaceOriginal;
                List<string> savedBgCopies = new List<string>();
                List<string> replacedPaths = new List<string>();
                int index = 0;
                foreach (var item in selectedTagsList)
                {
                    if (Extensions.VideoExtensions.Contains(Path.GetExtension(item.ImageFilePath).ToLowerInvariant()))
                    {
                        sbErrors.AppendLine(item.ImageFilePath);
                        continue;
                    }

                    var imgData = await bgRemoverForm.RemoveBackgroundAsync(item.ImageFilePath, selectedModel);
                    if (imgData == null)
                    {
                        sbErrors.AppendLine(item.ImageFilePath);
                    }
                    else
                    {
                        string outputPath = item.ImageFilePath;
                        if (!replace)
                        {
                            string dir = Path.GetDirectoryName(item.ImageFilePath) ?? string.Empty;
                            string baseName = Path.GetFileNameWithoutExtension(item.ImageFilePath);
                            outputPath = Path.Combine(dir, baseName + "_nobg.png");
                        }
                        try
                        {
                            // Atomic swap: a failed write must not leave a truncated image.
                            await Task.Run(() => SafeFile.WriteAllBytes(outputPath, imgData));
                        }
                        catch (Exception ex)
                        {
                            sbErrors.AppendLine($"{outputPath}: {ex.Message}");
                            SetStatus($"{++index} / {selectedTagsList.Count}");
                            continue;
                        }
                        if (replace)
                        {
                            // Only the overwritten source needs its thumbnail rebuilt.
                            // MakeThumb (ImageSharp) reads the just-written file once
                            // and does not lock it.
                            try
                            {
                                Program.DataManager.DataSet[item.ImageFilePath].Img = Extensions.MakeThumb(item.ImageFilePath, Program.Settings.PreviewSize);
                            }
                            catch (Exception)
                            {
                                Program.DataManager.DataSet[item.ImageFilePath].Img = null;
                            }
                            replacedPaths.Add(item.ImageFilePath);
                        }
                        else
                        {
                            savedBgCopies.Add(outputPath);
                        }
                    }
                    SetStatus($"{++index} / {selectedTagsList.Count}");
                }

                // Refresh the grid/preview after all writes have completed. All
                // reads below use images already in memory or a single fresh read
                // through the (already-cleared) cache, so nothing races the writes.
                if (replace)
                {
                    RefreshDatasetGrid();
                    // Reload the current preview if the selected image was processed
                    // (its cache entry was cleared above, so this re-reads the new file).
                    if (isShowPreview && gridViewDS.SelectedRows.Count == 1)
                    {
                        string cur = gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value as string;
                        if (!string.IsNullOrEmpty(cur) && replacedPaths.Contains(cur))
                            ShowPreview(cur);
                    }
                }
                else if (savedBgCopies.Count > 0)
                {
                    // Import the "_nobg.png" copies so they appear in the list.
                    IReadOnlyList<string> added = Program.DataManager.AddImages(savedBgCopies, true, false);
                    RefreshDatasetGrid(added);
                }
                //if (selectedTagsList.Count > 1)
                //{
                //    LoadSelectedImageToGrid();
                //}
                //if (gridViewAllTags.DataSource == null)
                //    BindTagList();
                }
                finally
                {
                    LockEdit(false);
                }
                if (sbErrors.Length > 0)
                {
                    sbErrors.Insert(0, "The following files were not processed (see AiApiServer log):\n");
                    MessageBox.Show(sbErrors.ToString());
                }
                return true;
            }
        }

        private async void generateTagsWithSettingsWindowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await GenerateTagsInTags(false, false);
        }

        private async void btnAutoGetTagsOpenSet_Click(object sender, EventArgs e)
        {
            await GetTagsWithAutoTagger(false);
        }

        private void MenuHideAllTags_Click(object sender, EventArgs e)
        {
            HideShowAllTagsWindow();
        }

        private void MenuHideTags_Click(object sender, EventArgs e)
        {
            HideShowTagsWindow();
        }

        private void MenuHideDataset_Click(object sender, EventArgs e)
        {
            HideShowDataset();
        }

        private void toolStripMenuItemWeight_ValueChanged(object sender, EventArgs e)
        {
            if (Program.DataManager == null)
            {
                return;
            }
            int wMod = toolStripMenuItemWeight.Value;
            float weight = 1;
            if (wMod == 0)
                weight = 1;
            else if (wMod > 0)
            {
                weight = WeightMultiplier(weight, PromptParser.round_bracket_multiplier, wMod);
            }
            else
            {
                weight = WeightMultiplier(weight, PromptParser.square_bracket_multiplier, Math.Abs(wMod));
            }
            toolStripTextBoxWeight.Text = weight.ToString();

            if (gridViewTags.SelectedCells.Count == 0)
                return;
            int rowIndex = gridViewTags.SelectedCells[0].RowIndex;
            var dsType = GetTagsDataSourceType();

            if (dsType == DataSourceType.Single)
            {
                ((EditableTagList)gridViewTags.DataSource)[rowIndex].Weight = weight;
            }
            else if (dsType == DataSourceType.Multi)
            {
                var dataItem = (DataItem)((MultiSelectDataRow)((MultiSelectDataTable)gridViewTags.DataSource).Rows[rowIndex]).GetDataItem();
                dataItem.Tags[((MultiSelectDataRow)((MultiSelectDataTable)gridViewTags.DataSource).Rows[rowIndex]).GetTagIndex()].Weight = weight;
            }
        }

        private float WeightMultiplier(float value, float multiplier, int count)
        {
            for (int i = 0; i < count; i++)
            {
                value *= multiplier;
            }
            return value;
        }

        private void gridViewTags_SelectionChanged(object sender, EventArgs e)
        {
            if (gridViewTags.SelectedCells.Count == 0)
                return;
            int rowIndex = gridViewTags.SelectedCells[0].RowIndex;
            var dsType = GetTagsDataSourceType();
            float weight = 1;
            if (dsType == DataSourceType.Single)
            {
                weight = ((EditableTagList)gridViewTags.DataSource)[rowIndex].Weight;
                toolStripTextBoxWeight.Text = weight.ToString();
            }
            else if (dsType == DataSourceType.Multi)
            {
                if (((MultiSelectDataTable)gridViewTags.DataSource).Rows[rowIndex].RowState == DataRowState.Detached && rowIndex < gridViewTags.RowCount)
                {
                    rowIndex++;
                }
                var dataItem = (DataItem)((MultiSelectDataRow)((MultiSelectDataTable)gridViewTags.DataSource).Rows[rowIndex]).GetDataItem();
                weight = dataItem.Tags[((MultiSelectDataRow)((MultiSelectDataTable)gridViewTags.DataSource).Rows[rowIndex]).GetTagIndex()].Weight;
                toolStripTextBoxWeight.Text = weight.ToString();
            }
            else
                return;
            if (weight == 1)
                toolStripMenuItemWeight.Value = 0;
            else if (weight > 1)
            {
                int brCount = Extensions.CalcBracketsCount(weight, true);
                if (brCount != 0)
                    toolStripMenuItemWeight.Value = brCount;
                else
                    toolStripMenuItemWeight.Value = 0;
            }
            else
            {
                int brCount = Extensions.CalcBracketsCount(weight, false);
                if (brCount != 0)
                    toolStripMenuItemWeight.Value = -brCount;
                else
                    toolStripMenuItemWeight.Value = 0;
            }
        }

        private async void generateTagsWithAutoTaggerForAllImagesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await GenerateTagsInTags(false, true);
        }

        private void toolStripButton2_Click(object sender, EventArgs e)
        {
            AddSelectedAutoTagsToImageTags();
        }

        private void AddSelectedAutoTagsToImageTags()
        {
            if (gridViewTags.DataSource == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }

            if (gridViewAutoTags.SelectedCells.Count == 0)
                return;
            List<string> selectedTags = new List<string>();
            for (int i = 0; i < gridViewAutoTags.SelectedCells.Count; i++)
            {
                string tag = (string)gridViewAutoTags["Tag", gridViewAutoTags.SelectedCells[i].RowIndex].Value;
                if (!selectedTags.Contains(tag))
                    selectedTags.Add(tag);
            }
            if (gridViewDS.SelectedRows.Count > 1)
            {
                foreach (var item in selectedTags)
                {
                    AddTagMultiselectedMode(item, true, AddingType.Down, 0);
                }

            }
            else
            {
                var eTagList = ((EditableTagList)gridViewTags.DataSource);
                foreach (var item in selectedTags)
                {
                    eTagList.AddTag(item, true, AddingType.Down, 0);
                }
            }
        }

        private void gridViewAutoTags_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            AddSelectedAutoTagsToImageTags();
        }

        private void gridViewAllTags_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.C)
            {
                if (gridViewAllTags.SelectedCells.Count > 0)
                {
                    List<string> tagsToCopy = new List<string>();
                    for (int i = 0; i < gridViewAllTags.SelectedCells.Count; i++)
                    {
                        tagsToCopy.Add((string)gridViewAllTags["TagsColumn", gridViewAllTags.SelectedCells[i].RowIndex].Value);
                    }
                    DataObject d = new DataObject();
                    d.SetText(string.Join("\r\n", tagsToCopy));
                    d.SetData("PartTagList", tagsToCopy);
                    Clipboard.SetDataObject(d);
                    SetStatus(I18n.GetText("StatusCopied"));
                }
                e.SuppressKeyPress = true;
            }
        }
        #region Debug
        private void testSortingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Form_ImageSorterSettings form_ImageSorter = new Form_ImageSorterSettings();
            form_ImageSorter.ShowDialog();
        }
        #endregion

        private void nameAscendingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }
            ((BindingSource)gridViewAllTags.DataSource).Sort = "Tag ASC";
        }

        private void countAscendingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }
            ((BindingSource)gridViewAllTags.DataSource).Sort = "Count ASC";
        }

        private void countDescendingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }
            ((BindingSource)gridViewAllTags.DataSource).Sort = "Count DESC";
        }

        private void openImageGridFormToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Form_TagImagesGrid imgGrid = new Form_TagImagesGrid();
            imgGrid.ShowDialog();
        }

        private void BtnTagImageChecker_Click(object sender, EventArgs e)
        {
            if (GetTagsDataSourceType() != DataSourceType.Multi)
            {
                return;
            }
            var dt = (MultiSelectDataTable)gridViewTags.DataSource;
            if (dt.Rows.Count == 0)
                return;

            // A pre-selected tag row is optional now: the editor has its own tag
            // list, the selection just decides which tag opens first.
            string initialTag = null;
            if (gridViewTags.SelectedCells.Count > 0)
            {
                int rowIndex = gridViewTags.SelectedCells[0].RowIndex;
                if (rowIndex >= 0 && rowIndex < dt.Rows.Count)
                    initialTag = ((MultiSelectDataRow)dt.Rows[rowIndex]).GetTagText();
            }

            using Form_TagImagesGrid imgGrid = new Form_TagImagesGrid();
            imgGrid.InitTagEditor(dt.GetSelectedDataItems(), initialTag);
            if (imgGrid.ShowDialog() != DialogResult.OK)
                return;
            foreach (var tagChanges in imgGrid.GetAllChanges())
            {
                dt.UpdateDataForTag(tagChanges.Key, tagChanges.Value);
            }
            gridViewTags.Refresh();
        }

        private void gridViewTags_CellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (GetTagsDataSourceType() == DataSourceType.Multi)
            {
                MultiSelectDataTable dt = (MultiSelectDataTable)gridViewTags.DataSource;
                MultiSelectDataRow dr = (MultiSelectDataRow)dt.Rows[e.RowIndex];
                e.ToolTipText = I18n.GetText("TipTagCountInSelected") + " " + dt.GetTagsCount(dr.GetTagText());
            }
        }

        private void BtnDSChangeSelection_Click(object sender, EventArgs e)
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }
            selectionMode = true;
            Form_TagImagesGrid imgGrid = new Form_TagImagesGrid();
            for (int i = 0; i < gridViewDS.RowCount; i++)
            {
                var dItem = Program.DataManager.DataSet[(string)gridViewDS["ImageFilePath", i].Value];
                bool selected = gridViewDS.Rows[i].Selected;
                imgGrid.AddDataItemChangeSelection(dItem, selected);
            }
            if (imgGrid.ShowDialog() != DialogResult.OK)
            {
                imgGrid.Close();
                selectionMode = false;
                return;
            }
            var result = imgGrid.GetResult(true);
            var selectionByPath = result.ToDictionary(a => a.Key.ImageFilePath, a => a.Value, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < gridViewDS.RowCount; i++)
            {
                string imgPath = (string)gridViewDS["ImageFilePath", i].Value;
                if (selectionByPath.TryGetValue(imgPath, out bool selected))
                    gridViewDS.Rows[i].Selected = selected;
            }
            LoadSelectedImageToGrid();
            selectionMode = false;
        }

        private async void cropImagesWithMoondream2ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var res = await CropImages();
            if (res)
                SetStatus("Cropping complete!");
            else
                SetStatus("Cropping canceled!");
        }

        private async void backgroundRemovalWithRMBG20ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var res = await RemoveBackgrounds(false);
            if (res)
                SetStatus(I18n.GetText("TipBGRemovalComplete"));
            else
                SetStatus(I18n.GetText("TipBGRemovalCanceled"));
        }

        private async void removeBackgroundToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var res = await RemoveBackgrounds(true);
            if (res)
                SetStatus(I18n.GetText("TipBGRemovalComplete"));
            else
                SetStatus(I18n.GetText("TipBGRemovalCanceled"));
        }

        private void openManualCropToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Was a hardcoded developer-machine path, which made this menu fail
            // for every user.
            using OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.gif|All files|*.*"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            using Form_ImageCrop form_ImageCrop = new Form_ImageCrop(dialog.FileName);
            form_ImageCrop.ShowDialog();
        }

        private void cropImageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (gridViewDS.SelectedRows.Count == 0 || Program.DataManager == null)
                return;

            string imagePath = (string)gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value;
            using (Form_ImageCrop form = new Form_ImageCrop(imagePath))
            {
                if (form.ShowDialog(this) != DialogResult.OK || form.ExportedPaths.Count == 0)
                    return;

                IReadOnlyList<string> added = Program.DataManager.AddImages(
                    form.ExportedPaths,
                    loadPreviewImages: true,
                    readMetadata: false);
                RefreshDatasetGrid();
                if (added.Count > 0)
                    SetStatus(string.Format(I18n.GetText("CropImageImportedCount"), added.Count));
            }
        }

        private async void btnOpenAiAutoGetTagsDefSet_Click(object sender, EventArgs e)
        {
            await GetTagsWithAutoTagger(true);
        }

        private async void btnOpenAiAutoGetTagsOpenSet_Click(object sender, EventArgs e)
        {
            await GetTagsWithAutoTagger(false);
        }

        private void BtnMenuOpenAiGenTagsWithCurrentSettings_Click(object sender, EventArgs e)
        {
            ShowLlmTagger();
        }

        private async void BtnMenuOpenAiGenTagsWithSetWindow_Click(object sender, EventArgs e)
        {
            await GenerateTagsInTags(false, false);
        }

        private async void MenuOpenAiGenTagsForAllImages_Click(object sender, EventArgs e)
        {
            await GenerateTagsInTags(false, true);
        }


    }
}
