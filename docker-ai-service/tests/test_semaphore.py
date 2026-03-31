"""Tests for the Python 3.12 semaphore fix — documents and verifies the workaround."""
import asyncio
import sys
import pytest


@pytest.mark.asyncio
async def test_semaphore_acquire_works_when_free():
    """Fixed pattern: _value > 0 check then acquire must always succeed."""
    sem = asyncio.Semaphore(4)
    assert sem._value == 4

    # Our fixed pattern
    assert sem._value > 0
    await sem.acquire()
    assert sem._value == 3

    sem.release()
    assert sem._value == 4


@pytest.mark.asyncio
async def test_semaphore_correctly_blocks_when_full():
    """Fixed pattern: _value <= 0 must indicate busy (would-be 429)."""
    sem = asyncio.Semaphore(2)
    await sem.acquire()
    await sem.acquire()
    assert sem._value == 0

    # Our pattern detects busy state
    assert sem._value <= 0  # → raise HTTPException(429)

    sem.release()
    sem.release()
    assert sem._value == 2


@pytest.mark.asyncio
async def test_semaphore_partial_use():
    """With max_concurrent=4, 3 concurrent requests still leave 1 free slot."""
    sem = asyncio.Semaphore(4)
    await sem.acquire()
    await sem.acquire()
    await sem.acquire()
    assert sem._value == 1
    assert sem._value > 0  # → would succeed

    sem.release()
    sem.release()
    sem.release()


@pytest.mark.asyncio
async def test_wait_for_timeout0_broken_on_py312():
    """Document that asyncio.wait_for(coro, timeout=0) is unreliable on Python 3.12+.
    Our fix avoids this pattern entirely."""
    sem = asyncio.Semaphore(4)  # plenty of free slots
    success_count = 0

    for _ in range(5):
        try:
            await asyncio.wait_for(sem.acquire(), timeout=0)
            sem.release()
            success_count += 1
        except asyncio.TimeoutError:
            pass

    py = (sys.version_info.major, sys.version_info.minor)
    if py >= (3, 12):
        # On Python 3.12+, timeout=0 cancels before the task runs — always fails
        # This documents the bug we fixed. If this assertion fails, Python fixed it
        # and we can simplify the workaround back to wait_for.
        assert success_count == 0, (
            f"wait_for(timeout=0) appears fixed in Python {py} — "
            "consider removing the _value workaround in main.py"
        )


def test_upscale_semaphore_is_initialized(client):
    """After app startup the global semaphore must exist and have free slots."""
    from app import main as app_module
    sem = app_module._upscale_semaphore
    assert sem is not None, "_upscale_semaphore was not initialized"
    assert sem._value > 0, f"semaphore has no free slots: _value={sem._value}"
    assert sem._value >= 1, "at least 1 concurrent slot expected"
