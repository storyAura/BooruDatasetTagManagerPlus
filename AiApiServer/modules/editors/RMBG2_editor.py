# pylint: disable=bad-indentation
import os

from PIL import Image
from transformers import AutoModelForImageSegmentation
from torchvision import transforms
import torch
#import matplotlib.pyplot as plt

from .. import settings
from .. import devices as devices
from .. import utilities, paths



class RMBG2Editor:

    def __init__(self, model_name, source: str = "hf"):
        # model_name: repo id on the selected hub (HF or ModelScope).
        # source: "hf" for the HuggingFace main site, "modelscope" for the
        # mainland-China mirror on modelscope.cn.
        self.MODEL_REPO = model_name
        self.source = (source or "hf").lower()
        self.model = None
        self.transform_image = None
        self.resolution = (1024, 1024)

    def _resolve_model_path(self, skip_online: bool) -> str:
        # For HuggingFace we let from_pretrained handle download/caching via
        # cache_dir. For ModelScope we download a snapshot first and load the
        # model from the resulting local directory.
        if self.source != "modelscope":
            return self.MODEL_REPO

        try:
            from modelscope import snapshot_download
        except ImportError as exc:
            raise RuntimeError(
                "ModelScope source selected but the 'modelscope' package is not "
                "installed. Run: pip install modelscope"
            ) from exc

        cache_dir = str(paths.setting_model_path) if paths.setting_model_path else None
        local_dir = snapshot_download(
            self.MODEL_REPO,
            cache_dir=cache_dir,
            local_files_only=skip_online,
        )
        return local_dir

    def load(self, resolution, skip_online: bool = False):
        self.resolution = resolution
        if self.model is None:
            torch.set_float32_matmul_precision(["high", "highest"][0])
            model_path = self._resolve_model_path(skip_online)
            self.model = AutoModelForImageSegmentation.from_pretrained(model_path,
                                                              trust_remote_code=True,
                                                              cache_dir=paths.setting_model_path,
                                                              local_files_only=skip_online
                                                              ).to(devices.device)
            self.model.eval()
            self.transform_image = transforms.Compose([
                transforms.Resize(self.resolution),
                transforms.ToTensor(),
                transforms.Normalize([0.485, 0.456, 0.406], [0.229, 0.224, 0.225])
            ])


    def unload(self):
        if not settings.current.interrogator_keep_in_memory:
            self.model = None
            devices.torch_gc()

    def apply(self, image: Image.Image):
        if self.model is None:
            return ""
        # The model input transform ends with a 3-channel Normalize, so the
        # image must be RGB. RGBA / grayscale / palette inputs would otherwise
        # crash with a channel-count mismatch during Normalize.
        rgb_image = image if image.mode == "RGB" else image.convert("RGB")
        input_images = self.transform_image(rgb_image).unsqueeze(0).to(devices.device)
        with torch.no_grad():
            preds = self.model(input_images)[-1].sigmoid().cpu()
        pred = preds[0].squeeze()
        pred_pil = transforms.ToPILImage()(pred)
        mask = pred_pil.resize(rgb_image.size)
        result = rgb_image.copy()
        result.putalpha(mask)
        return result
