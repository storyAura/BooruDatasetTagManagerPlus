using System.Collections.Generic;
using System.Linq;
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BooruDatasetTagManager
{
    public static class AiPromptTemplateCatalog
    {
        public const string DanbooruTag = "Danbooru Tag";
        public const string DanbooruTagId = "builtin.danbooru-tag";
        public const string NaturalLanguageId = "builtin.natural-language";
        public const string HybridModeId = "builtin.hybrid-mode";
        public const string NaturalLanguage2Id = "builtin.natural-language-2";
        public static string AutoTaggingSystemPrompt => templates[0].SystemPrompt;
        public static string LlmT2NlSystemPrompt => templates[1].SystemPrompt;
        public const string NaturalLanguage = "自然语言";
        public const string HybridMode = "混合模式";
        public const string NaturalLanguage2 = "自然语言2";

        private static readonly List<AiPromptTemplate> templates = new List<AiPromptTemplate>
        {
            new AiPromptTemplate(DanbooruTag,
@"Analyze the image and provide a list of precise, comma-separated Danbooru-style tags.
Rules:
1. Use ONLY Danbooru tags (e.g., 1girl, blue_hair, pleated_skirt, holding_book).
2. All tags must be lowercase, and spaces must be replaced with underscores.
3. Identify character features, hairstyle, clothing, pose, and background elements.
4. DO NOT include quality modifiers (e.g. masterpiece, best quality) or natural language sentences.
Format: tag1, tag2, tag3..."),
            new AiPromptTemplate(NaturalLanguage,
@"Describe this image in a detailed, objective, and realistic natural language paragraph for AI training.
Rules:
1. Start directly with the main subject and their action (e.g., ""A photograph of a young woman with blue hair sitting at a desk..."").
2. Describe details in order: subject (clothing, expression, hairstyle, posture), immediate surroundings, background elements, lighting, and style.
3. Avoid quality buzzwords (e.g. masterpiece, photorealistic, ultra-detailed) and subjective emotional opinions. Keep the description flowing naturally as a coherent paragraph.
4. Do not output a comma-separated tag list. Reference tags are hints only and must be rewritten as natural prose."),
            new AiPromptTemplate(HybridMode,
@"Analyze this image for a LoRA training dataset. Provide a hybrid description combining core Danbooru-style tags and detailed natural language.
Rules:
1. First part: Output the most important Danbooru tags representing the character, hairstyle, and key outfits, separated by commas (e.g., ""1girl, solo, short_hair, blue_skirt"").
2. Second part: Followed by a comma, write a descriptive natural language sentence detailing the action, specific pose, background scene, and lighting.
3. DO NOT use quality modifiers like masterpiece, best quality.
Format: tag1, tag2, [natural language description]"),
            new AiPromptTemplate(NaturalLanguage2,
@"您是一套图像分析系统。您的功能是处理视觉数据并输出完全客观、全面的英文文本描述，切记输出英文。 您的主要目标是生成一个简洁明了的段落，最多不能超过200词,对所提供的图像进行描述。 请严格遵守以下指令： **1. 绝对客观原则：** 仅描述视觉上呈现的内容。您的语言必须客观、精确且陈述明确。**严禁**任何形式的推测、推断或假设。请勿使用“似乎是”、“看起来”、“可能是”、“很可能”或“暗示”等词语。如实陈述观察到的事实。 **2. 内容处理政策：** 您必须以客观冷静的态度描述所有可见的元素、动作和互动。无论内容是否敏感、露骨、暴力或粗俗，都不得审查、省略或做出任何道德评判。您的描述是对视觉信息的客观报告。 **3. 必须包含的细节：** 您的描述必须包含以下内容： * **人物与实体：** 描述所有人物、生物或角色以及他们的神态，动作。如果他们是公众人物或虚构人物（请不要描述他们的出处和具体姓名），请说明。详细描述他们的外貌、衣着、姿势、动作和表情。 * **物体与环境：** 详细描述场景（例如，室内、室外、城市、自然环境）、所有物体及其彼此之间以及与角色之间的空间关系。 * **摄影属性：** 请提供客观的摄影细节，例如拍摄角度（例如，低角度拍摄、平视拍摄）、构图、景深（例如，浅景深背景虚化）、对焦、光线以及镜头效果（例如，鱼眼畸变、镜头光晕）。 * **审查：** 如果图像的任何部分被审查（例如，像素化、黑边、马赛克），您必须明确说明审查情况，并描述审查覆盖了图像的哪个部分，如果未有审查，则无需添加描述。 **4. 禁止内容：** 您必须禁止： * **主观解读：** 请勿分析或猜测图像背后的含义、象征意义、意图或叙事。禁止赞扬或批评。 * **艺术风格：** 请勿按艺术流派（例如“印象派”、“超现实主义”）或非写实渲染风格（例如“卡通渲染”、“像素艺术”）对图像进行分类。 **5. 输出格式：** 仅提供详细描述。所有输出必须为一个完整的段落。请勿在描述前后添加任何标题、摘要、引言或其他文字。 优秀的标签标注将获得每张图片 10 美元的奖励。")
        };

        public static IReadOnlyList<AiPromptTemplate> Templates => templates;

        public static AiPromptTemplate GetByName(string name)
        {
            return templates.FirstOrDefault(template => string.Equals(template.Name, name, StringComparison.OrdinalIgnoreCase)) ?? templates[0];
        }

        public static IReadOnlyList<AiPromptTemplateSettings> CreateDefaultSettings()
        {
            string[] ids = { DanbooruTagId, NaturalLanguageId, HybridModeId, NaturalLanguage2Id };
            return templates.Select((template, index) => new AiPromptTemplateSettings
            {
                Id = ids[index],
                Name = template.Name,
                SystemPrompt = template.SystemPrompt,
                IsBuiltIn = true
            }).ToList();
        }

        public static AiPromptTemplateSettings GetDefaultById(string id)
        {
            return CreateDefaultSettings().FirstOrDefault(template => template.Id == id);
        }
    }

    public sealed class AiPromptTemplate
    {
        public AiPromptTemplate(string name, string systemPrompt)
        {
            Name = name;
            SystemPrompt = systemPrompt;
        }

        public string Name { get; }
        public string SystemPrompt { get; }
    }

    public sealed class AiPromptTemplateSettings
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SystemPrompt { get; set; } = string.Empty;
        public bool IsBuiltIn { get; set; }

        public AiPromptTemplateSettings Clone()
        {
            return new AiPromptTemplateSettings
            {
                Id = Id,
                Name = Name,
                SystemPrompt = SystemPrompt,
                IsBuiltIn = IsBuiltIn
            };
        }
    }

    public sealed class AiPromptTemplateLibrary
    {
        private readonly List<AiPromptTemplateSettings> templates;

        private AiPromptTemplateLibrary(List<AiPromptTemplateSettings> templates, string selectedTemplateId)
        {
            this.templates = templates;
            SelectedTemplateId = selectedTemplateId;
        }

        public IReadOnlyList<AiPromptTemplateSettings> Templates => templates;
        public string SelectedTemplateId { get; private set; }
        public AiPromptTemplateSettings SelectedTemplate =>
            templates.First(template => template.Id == SelectedTemplateId);

        public static AiPromptTemplateLibrary Create(
            IEnumerable<AiPromptTemplateSettings> savedTemplates,
            string selectedTemplateId,
            string legacySelectedName)
        {
            List<AiPromptTemplateSettings> defaults = AiPromptTemplateCatalog.CreateDefaultSettings()
                .Select(template => template.Clone())
                .ToList();
            List<AiPromptTemplateSettings> saved = (savedTemplates ?? Enumerable.Empty<AiPromptTemplateSettings>())
                .Where(template => template != null)
                .ToList();

            foreach (AiPromptTemplateSettings template in defaults)
            {
                AiPromptTemplateSettings savedTemplate = saved.FirstOrDefault(item => item.Id == template.Id);
                if (savedTemplate != null && !string.IsNullOrWhiteSpace(savedTemplate.SystemPrompt))
                    template.SystemPrompt = savedTemplate.SystemPrompt;
            }

            foreach (AiPromptTemplateSettings custom in saved.Where(item => !IsBuiltInId(item.Id)))
            {
                string name = custom.Name?.Trim();
                string prompt = custom.SystemPrompt?.Trim();
                if (string.IsNullOrWhiteSpace(custom.Id)
                    || string.IsNullOrWhiteSpace(name)
                    || string.IsNullOrWhiteSpace(prompt)
                    || defaults.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                defaults.Add(new AiPromptTemplateSettings
                {
                    Id = custom.Id,
                    Name = name,
                    SystemPrompt = prompt,
                    IsBuiltIn = false
                });
            }

            string selectedId = defaults.Any(template => template.Id == selectedTemplateId)
                ? selectedTemplateId
                : defaults.FirstOrDefault(template => string.Equals(
                    template.Name,
                    legacySelectedName,
                    StringComparison.OrdinalIgnoreCase))?.Id;
            selectedId ??= AiPromptTemplateCatalog.DanbooruTagId;
            return new AiPromptTemplateLibrary(defaults, selectedId);
        }

        public AiPromptTemplateSettings AddCustom(string name, string systemPrompt)
        {
            Validate(name, systemPrompt, null);
            AiPromptTemplateSettings template = new AiPromptTemplateSettings
            {
                Id = "custom." + Guid.NewGuid().ToString("N"),
                Name = name.Trim(),
                SystemPrompt = systemPrompt.Trim(),
                IsBuiltIn = false
            };
            templates.Add(template);
            SelectedTemplateId = template.Id;
            return template;
        }

        public void Update(string id, string name, string systemPrompt)
        {
            AiPromptTemplateSettings template = Find(id);
            string effectiveName = template.IsBuiltIn ? template.Name : name;
            Validate(effectiveName, systemPrompt, id);
            if (!template.IsBuiltIn)
                template.Name = effectiveName.Trim();
            template.SystemPrompt = systemPrompt.Trim();
        }

        public bool Delete(string id)
        {
            AiPromptTemplateSettings template = templates.FirstOrDefault(item => item.Id == id);
            if (template == null || template.IsBuiltIn)
                return false;

            templates.Remove(template);
            if (SelectedTemplateId == id)
                SelectedTemplateId = AiPromptTemplateCatalog.DanbooruTagId;
            return true;
        }

        public bool RestoreDefault(string id)
        {
            AiPromptTemplateSettings template = templates.FirstOrDefault(item => item.Id == id);
            AiPromptTemplateSettings defaultTemplate = AiPromptTemplateCatalog.GetDefaultById(id);
            if (template == null || defaultTemplate == null)
                return false;

            template.SystemPrompt = defaultTemplate.SystemPrompt;
            return true;
        }

        public void Select(string id)
        {
            Find(id);
            SelectedTemplateId = id;
        }

        public List<AiPromptTemplateSettings> CreateSnapshot()
        {
            return templates.Select(template => template.Clone()).ToList();
        }

        public string ExportCurrentJson()
        {
            return SerializeExport(new[] { SelectedTemplate });
        }

        public string ExportAllCustomJson()
        {
            return SerializeExport(templates.Where(template => !template.IsBuiltIn));
        }

        private static bool IsBuiltInId(string id)
        {
            return AiPromptTemplateCatalog.CreateDefaultSettings().Any(template => template.Id == id);
        }

        private AiPromptTemplateSettings Find(string id)
        {
            return templates.FirstOrDefault(template => template.Id == id)
                ?? throw new ArgumentException("Prompt template was not found.", nameof(id));
        }

        private void Validate(string name, string systemPrompt, string excludedId)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Prompt template name is required.", nameof(name));
            if (string.IsNullOrWhiteSpace(systemPrompt))
                throw new ArgumentException("Prompt template content is required.", nameof(systemPrompt));
            if (templates.Any(template => template.Id != excludedId
                && string.Equals(template.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("Prompt template name must be unique.", nameof(name));
            }
        }

        private static string SerializeExport(IEnumerable<AiPromptTemplateSettings> exportTemplates)
        {
            var package = new
            {
                Version = 1,
                Templates = exportTemplates.Select(template => template.Clone()).ToList()
            };
            return JsonConvert.SerializeObject(package, Formatting.Indented, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });
        }
    }
}
