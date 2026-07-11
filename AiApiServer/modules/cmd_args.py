import argparse
import functools


@functools.lru_cache(maxsize=1)
def get_args():
    parser = argparse.ArgumentParser()

    parser.add_argument(
        "--device-id", type=int, help="CUDA Device ID to use interrogators", default=None
    )
    parser.add_argument(
        "--force-install-torch",
        choices=['cu117', 'cu118', 'cu120', 'cpu'],
        help="Force install the latest PyTorch with specified compute platform (if not installed in this computer)",
        default=None,
    )
    parser.add_argument(
        "--listen",
        action="store_true",
        help="Listen on all network interfaces (0.0.0.0) instead of localhost only",
    )
    parser.add_argument(
        "--port", type=int, help="Port to listen on", default=50051
    )
    parser.add_argument(
        "--api-key",
        type=str,
        help="If set, all requests must include this value in the X-Api-Key header",
        default=None,
    )

    opts, _ = parser.parse_known_args()

    return opts
