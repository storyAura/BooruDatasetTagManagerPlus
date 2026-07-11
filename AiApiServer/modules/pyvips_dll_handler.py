#  To fix : https://github.com/libvips/pyvips/issues/489
#  You can manually download binaries from : https://github.com/libvips/build-win64-mxe/releases/tag/v8.16.0

import platform
import os
import requests
import zipfile


PYVIPS_WIN_DLL_URL = {
    "AMD64": "https://github.com/libvips/build-win64-mxe/releases/download/v8.16.0/vips-dev-w64-all-8.16.0.zip",
    "X86": "https://github.com/libvips/build-win64-mxe/releases/download/v8.16.0/vips-dev-w32-all-8.16.0.zip"
}

# (connect timeout, read timeout) seconds. Without a timeout an unreachable
# GitHub blocked the whole server forever (the download used to run at import
# time inside `import models`).
DOWNLOAD_TIMEOUT = (15, 120)


def handle_pyvips_dll_error(download_dir: str):
    """Download Windows dll for pyvips and add the bin directory to the PATH."""
    pyvips_dll_dir = os.path.join(download_dir, "vips-dev-8.16")
    pyvips_bin_dir = os.path.join(pyvips_dll_dir, "bin")

    # Validate the bin directory (not just "directory exists"): an interrupted
    # extraction used to leave a half-populated tree that was never repaired.
    if not os.path.isdir(pyvips_bin_dir) or not os.listdir(pyvips_bin_dir):
        system = platform.system()

        if system.upper() == "WINDOWS":
            print(f"pyvips dll directory not detected. Downloading it to \"{pyvips_dll_dir}\"..")

            arch = os.environ.get("PROCESSOR_ARCHITECTURE", "")
            arch = arch.upper()
            url = PYVIPS_WIN_DLL_URL.get(arch, PYVIPS_WIN_DLL_URL["AMD64"])
        else:
            return

        zip_filename = os.path.join(download_dir, "pyvips_dll.zip")

        try:
            response = requests.get(url, stream=True, timeout=DOWNLOAD_TIMEOUT)
            response.raise_for_status()

            with open(zip_filename, 'wb') as f:
                for chunk in response.iter_content(chunk_size=8192):
                    f.write(chunk)

            with zipfile.ZipFile(zip_filename, 'r') as zip_ref:
                zip_ref.extractall(download_dir)
        finally:
            try:
                if os.path.exists(zip_filename):
                    os.remove(zip_filename)
            except OSError:
                pass

        if not os.path.isdir(pyvips_bin_dir) or not os.listdir(pyvips_bin_dir):
            raise RuntimeError(
                f"pyvips runtime extraction failed: '{pyvips_bin_dir}' is missing or empty. "
                "Delete the 'vips-dev-8.16' directory and retry, or install the vips "
                "binaries manually (see the URLs at the top of this file).")

    # Add PATH
    os.environ['PATH'] = os.pathsep.join((pyvips_bin_dir, os.environ['PATH']))
