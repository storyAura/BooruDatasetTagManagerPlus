using System;
using System.Collections.Generic;
using System.Linq;

namespace BooruDatasetTagManager
{
    public enum OnnxTaggerModelKind
    {
        Wd14,
        PixAi,
        ClTagger,
        OppaiOracle
    }

    public sealed class OnnxTaggerModelEntry
    {
        public string Id { get; init; }
        public OnnxTaggerModelKind Kind { get; init; }
        public string DisplayName { get; init; }
        public string Repo { get; init; }
        public double DefaultThreshold { get; init; }
        public double? DefaultCharacterThreshold { get; init; }
        /// <summary>Non-null for Kind == PixAi.</summary>
        public PixAiModelDefinition PixAiModel { get; init; }
        /// <summary>Non-null for Kind == ClTagger.</summary>
        public ClTaggerModelDefinition ClModel { get; init; }
        /// <summary>Non-null for Kind == OppaiOracle.</summary>
        public OppaiOracleModelDefinition OppaiModel { get; init; }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    public static class OnnxTaggerCatalog
    {
        public const string PixAiModelId = PixAiOnnxTaggerService.V09Id;

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

            foreach (PixAiModelDefinition model in PixAiOnnxTaggerService.Models)
            {
                models.Add(new OnnxTaggerModelEntry
                {
                    Id = model.Id,
                    Kind = OnnxTaggerModelKind.PixAi,
                    DisplayName = "[PixAI] " + model.ShortName,
                    Repo = model.Repo,
                    DefaultThreshold = model.DefaultGeneralThreshold,
                    DefaultCharacterThreshold = model.DefaultCharacterThreshold,
                    PixAiModel = model
                });
            }

            foreach (ClTaggerModelDefinition model in ClTaggerOnnxService.Models)
            {
                models.Add(new OnnxTaggerModelEntry
                {
                    Id = model.Id,
                    Kind = OnnxTaggerModelKind.ClTagger,
                    // Mark the gated repo so the restriction is visible up front.
                    DisplayName = "[CL] " + model.ShortName + (model.IsGated ? " 🔒" : string.Empty),
                    Repo = model.Repo,
                    DefaultThreshold = model.DefaultThreshold,
                    DefaultCharacterThreshold = model.DefaultCharacterThreshold,
                    ClModel = model
                });
            }

            foreach (OppaiOracleModelDefinition model in OppaiOracleOnnxService.Models)
            {
                models.Add(new OnnxTaggerModelEntry
                {
                    Id = model.Id,
                    Kind = OnnxTaggerModelKind.OppaiOracle,
                    DisplayName = "[OO] " + model.ShortName,
                    Repo = model.Repo,
                    DefaultThreshold = model.DefaultThreshold,
                    OppaiModel = model
                });
            }

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
                ? Wd14OnnxTaggerService.DefaultRepo
                : wd14SelectedRepo;
            return "wd:" + repo;
        }
    }
}
