from .editors import RMBG2Editor


class editor:
    def __enter__(self):
        self.start()
        return self

    def __exit__(self, exception_type, exception_value, traceback):
        self.stop()
        pass

    def start(self, net_params: dict, skip_online: bool = False):
        pass

    def stop(self):
        pass

    def predict(self, image):
        raise NotImplementedError()

    def name(self):
        raise NotImplementedError()

    def mode_type(self):
        raise NotImplementedError()


class RMBG2(editor):
    def __init__(self, display_name, repo_id, source, resulution, repo_link, intType):
        self.editor = RMBG2Editor(repo_id, source)
        self.display_name = display_name
        self.repo_id = repo_id
        self.source = source
        # repo_name kept empty so main.py falls back to the explicit repo_link below.
        self.repo_name = ""
        # Full repository URL shown in the client (HuggingFace or ModelScope).
        self.repo_link = repo_link
        self.resolution = resulution
        self.type = intType
        self.video_supported = False

    def start(self, net_params: dict, skip_online: bool = False):
        self.editor.load(self.resolution, skip_online=skip_online)

    def stop(self):
        self.editor.unload()

    def predict(self, image):
        res = self.editor.apply(image)
        # tags = res[0].split(",")
        return res  # [t for t in tags if t]

    def name(self):
        return self.display_name

    def mode_type(self):
        return self.type
