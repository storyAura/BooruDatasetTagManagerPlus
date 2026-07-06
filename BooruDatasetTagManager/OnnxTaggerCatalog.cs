using System;
using System.Collections.Generic;
using System.Linq;

namespace BooruDatasetTagManager
{
    public enum OnnxTaggerModelKind
    {
        Wd14,
        PixAi
    }

    public sealed class OnnxTaggerModelEntry
    {
        public string Id { get; init; }
        public OnnxTaggerModelKind Kind { get; init; }
        public string DisplayName { get; init; }
        public string Repo { get; init; }
        public double DefaultThreshold { get; init; }
        public double? DefaultCharacterThreshold { get; init; }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    public static class OnnxTaggerCatalog
    {
        public const string PixAiModelId = "pixai:v0.9";

        public static IReadOnlyList<OnnxTaggerModelEntry> AllModels { get; }

        static OnnxTaggerCatalog()
        {
            var models = new List<OnnxTaggerModelEntry>();
            foreach (Wd14ModelDefinition model in Wd14OnnxTaggerService.Models)
            {
                models.Add(new OnnxTaggerModelEntry
                {
                    Id = "wd:" + model.Repo,
                    Kind = OnnxTaggerModelKind.Wd14,
                    DisplayName = "[WD14] " + model.ShortName,
                    Repo = model.Repo,
                    DefaultThreshold = model.DefaultThreshold,
                    DefaultCharacterThreshold = model.DefaultCharacterThreshold
                });
            }

            models.Add(new OnnxTaggerModelEntry
            {
                Id = PixAiModelId,
                Kind = OnnxTaggerModelKind.PixAi,
                DisplayName = "[PixAI] v0.9",
                Repo = PixAiOnnxTaggerService.ModelRepo,
                DefaultThreshold = 0.3,
                DefaultCharacterThreshold = 0.85
            });

            AllModels = models;
        }

        public static OnnxTaggerModelEntry GetById(string id)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                OnnxTaggerModelEntry match = AllModels.FirstOrDefault(model =>
                    string.Equals(model.Id, id, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match;
            }

            return AllModels[0];
        }

        public static string ResolveInitialModelId(string lastModelId, string wd14SelectedRepo)
        {
            if (!string.IsNullOrWhiteSpace(lastModelId)
                && AllModels.Any(model => string.Equals(model.Id, lastModelId, StringComparison.OrdinalIgnoreCase)))
            {
                return lastModelId;
            }

            string repo = string.IsNullOrWhiteSpace(wd14SelectedRepo)
                ? Wd14OnnxTaggerService.Models[^1].Repo
                : wd14SelectedRepo;
            return "wd:" + repo;
        }
    }
}
