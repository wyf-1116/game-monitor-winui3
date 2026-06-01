from __future__ import annotations

import ctypes
from ctypes import wintypes


FILE_MAP_READ = 0x0004


class SharedMemoryError(RuntimeError):
    pass


kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

OpenFileMappingW = kernel32.OpenFileMappingW
OpenFileMappingW.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.LPCWSTR]
OpenFileMappingW.restype = wintypes.HANDLE

MapViewOfFile = kernel32.MapViewOfFile
MapViewOfFile.argtypes = [
    wintypes.HANDLE,
    wintypes.DWORD,
    wintypes.DWORD,
    wintypes.DWORD,
    ctypes.c_size_t,
]
MapViewOfFile.restype = wintypes.LPVOID

UnmapViewOfFile = kernel32.UnmapViewOfFile
UnmapViewOfFile.argtypes = [wintypes.LPCVOID]
UnmapViewOfFile.restype = wintypes.BOOL

CloseHandle = kernel32.CloseHandle
CloseHandle.argtypes = [wintypes.HANDLE]
CloseHandle.restype = wintypes.BOOL


def _last_error(prefix: str) -> SharedMemoryError:
    code = ctypes.get_last_error()
    return SharedMemoryError(f"{prefix} failed with Windows error {code}")


def read_named_mapping(name: str, size: int) -> bytes:
    handle = OpenFileMappingW(FILE_MAP_READ, False, name)
    if not handle:
        raise _last_error(f"OpenFileMappingW({name})")

    address = None
    try:
        address = MapViewOfFile(handle, FILE_MAP_READ, 0, 0, size)
        if not address:
            raise _last_error(f"MapViewOfFile({name})")

        buffer_type = ctypes.c_ubyte * size
        buffer = buffer_type.from_address(address)
        return bytes(buffer)
    finally:
        if address:
            UnmapViewOfFile(address)
        CloseHandle(handle)
