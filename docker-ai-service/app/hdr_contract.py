"""Explicit transport boundaries for the single-frame PQ pipeline."""
PNG_SIGNATURE = b'\x89PNG\r\n\x1a\n'


def require_pq(transfer: str):
    if transfer.lower() != 'smpte2084':
        raise ValueError('Only PQ/ST.2084 (smpte2084) is supported; HLG, unknown transfers and dynamic HDR are not supported.')


def require_rgb16_png(data: bytes):
    if len(data) < 33 or data[:8] != PNG_SIGNATURE or data[24:26] != b'\x10\x02':
        raise ValueError('HDR requires a 16-bit RGB PNG; JPEG, 8-bit, grayscale and alpha are not supported.')


def reject_realtime_hdr(data: bytes = b'', transfer: str = ''):
    if transfer.lower() not in ('', 'bt709', 'srgb') or (len(data) >= 26 and data[:8] == PNG_SIGNATURE and data[24] > 8):
        raise ValueError('HDR realtime and multi-frame processing are not supported. Use individual PQ RGB16 PNG frames via /upscale-hdr.')
