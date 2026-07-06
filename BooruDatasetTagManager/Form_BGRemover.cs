using BooruDatasetTagManager.AiApi;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    public partial class Form_BGRemover : Form
    {
        private readonly MainForm owner;
        private bool connectSuccess;

        public Form_BGRemover(MainForm owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            InitializeComponent();
            Program.ColorManager.ChangeColorScheme(this, Program.ColorManager.SelectedScheme);
            Program.ColorManager.ChangeColorSchemeInConteiner(Controls, Program.ColorManager.SelectedScheme);
            SwitchLanguage();
        }

        private void SwitchLanguage()
        {
            Text = I18n.GetText("UIBGRemovalForm");
            buttonCheckConnection.Text = I18n.GetText("UIBGRemovalFormCheckBtn");
            groupBox1.Text = I18n.GetText("UIBGRemovalFormGroupText");
            label4.Text = I18n.GetText("UIBGRemovalFormModeLabel");
            radioButtonAllImages.Text = I18n.GetText("UICropImagesFormRadioAll");
            radioButtonOnlySelected.Text = I18n.GetText("UICropImagesFormRadioSelected");
            label1.Text = I18n.GetText("UIBGRemovalFormModelsLabel");
            buttonRemovingTest.Text = I18n.GetText("UIBGRemovalFormRemovingTestBtn");
            button2.Text = I18n.GetText("BtnOK");
            button3.Text = I18n.GetText("BtnCancel");
            if (connectSuccess)
                buttonCheckConnection.Text = I18n.GetText("UIBGRemovalFormConnected");
        }

        private async void buttonCheckConnection_Click(object sender, EventArgs e)
        {
            buttonCheckConnection.Enabled = false;
            buttonCheckConnection.Text = I18n.GetText("UIBGRemovalFormChecking");
            listBoxModels.Items.Clear();

            if (!Program.AutoTagger.IsConnected)
            {
                if (!await Program.AutoTagger.ConnectAsync())
                {
                    MessageBox.Show(I18n.GetText("TipAutoTagUnableConnect"));
                    buttonCheckConnection.Enabled = true;
                    buttonCheckConnection.Text = I18n.GetText("UIBGRemovalFormCheckBtn");
                    return;
                }
            }

            if (Program.AutoTagger.IsConnected)
            {
                var models = (await Program.AutoTagger.GetListModelsByType("rmbg2")).GetList().ToArray();
                if (models.Length == 0)
                {
                    MessageBox.Show(I18n.GetText("UIBGRemovalFormNoModels"));
                    buttonCheckConnection.Enabled = true;
                    buttonCheckConnection.Text = I18n.GetText("UIBGRemovalFormCheckBtn");
                    return;
                }

                connectSuccess = true;
                listBoxModels.Items.AddRange(models);
                listBoxModels.SelectedIndex = 0;
                groupBox1.Enabled = true;
                buttonCheckConnection.Text = I18n.GetText("UIBGRemovalFormConnected");
            }
            else
            {
                buttonCheckConnection.Enabled = true;
                buttonCheckConnection.Text = I18n.GetText("UIBGRemovalFormCheckBtn");
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
        }

        private void button3_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }

        private async void button4_Click(object sender, EventArgs e)
        {
            string selectedModel = GetSelectedModel();
            if (string.IsNullOrEmpty(selectedModel))
                return;

            string imagePath = owner.GetSelectedDatasetImagePaths().FirstOrDefault();
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                MessageBox.Show(I18n.GetText("TaggerNoImages"), I18n.GetText("UIError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            buttonRemovingTest.Enabled = false;
            var res = await RemoveBackgroundAsync(imagePath, selectedModel);
            if (res == null)
            {
                buttonRemovingTest.Enabled = true;
                return;
            }

            Image img;
            using (var ms = new MemoryStream(res))
            {
                img = Image.FromStream(ms);
            }

            buttonRemovingTest.Enabled = true;
            using Form_preview preview = new Form_preview();
            preview.Show(img);
        }

        public string GetSelectedModel()
        {
            if (listBoxModels.SelectedIndex == -1)
                return null;

            if (listBoxModels.SelectedItem is ModelBaseInfo model)
                return model.ModelName;

            return listBoxModels.SelectedItem?.ToString();
        }

        public async Task<byte[]> RemoveBackgroundAsync(string imgFilePath, string model)
        {
            ModelParameters modelParam = new ModelParameters { ModelName = model };
            var result = await Program.AutoTagger.EditImage(
                imgFilePath,
                modelParam,
                Program.Settings.AutoTagger.SerializeVramUsage,
                Program.Settings.AutoTagger.SkipInternetRequests);
            if (result.Success)
                return result.ImageData;
            return null;
        }

        private void Form_BGRemover_Load(object sender, EventArgs e)
        {
            buttonCheckConnection.PerformClick();
        }
    }
}
