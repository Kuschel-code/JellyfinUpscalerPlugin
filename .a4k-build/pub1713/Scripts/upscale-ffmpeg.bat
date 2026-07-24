@echo off
REM ###########################################################################
REM AI Upscaler FFmpeg Wrapper for Windows
REM Intercepts Jellyfin transcoding commands and injects upscaling filters
REM
REM INSTALLATION:
REM 1. Edit REAL_FFMPEG_PATH to point to actual ffmpeg.exe
REM 2. Set this script as FFmpeg path in Jellyfin Admin Dashboard
REM ###########################################################################

SET "REAL_FFMPEG_PATH=C:\ProgramData\Jellyfin\Server\ffmpeg.exe"
SET "UPSCALER_PLUGIN_API=http://localhost:8096/api/Upscaler"
SET "ENABLE_UPSCALING=true"
SET "UPSCALE_FACTOR=2"
SET "LOG_FILE=%ProgramData%\Jellyfin\Server\log\upscaler-wrapper.log"

REM Check if real FFmpeg exists
if not exist "%REAL_FFMPEG_PATH%" (
    echo [%date% %time%] ERROR: Real FFmpeg not found at %REAL_FFMPEG_PATH% >> "%LOG_FILE%"
    exit /b 1
)

REM Parse arguments to detect transcoding
SET "ARGS=%*"
SET "IS_TRANSCODING=false"

echo %ARGS% | findstr /C:".m3u8" >nul && SET "IS_TRANSCODING=true"
echo %ARGS% | findstr /C:".ts" >nul && SET "IS_TRANSCODING=true"

REM Inject upscaling filter if enabled
if "%ENABLE_UPSCALING%"=="true" if "%IS_TRANSCODING%"=="true" (
    echo [%date% %time%] Detected transcoding, injecting AI upscaling filter >> "%LOG_FILE%"
    
    REM Query hardware capabilities
    for /f "delims=" %%i in ('curl -s "%UPSCALER_PLUGIN_API%/hardware"') do set "HARDWARE_INFO=%%i"
    
    REM Check for CUDA support
    echo %HARDWARE_INFO% | findstr /C:"SupportsCUDA.*true" >nul
    if %errorlevel%==0 (
        SET "UPSCALE_FILTER=hwupload_cuda,scale_cuda=%UPSCALE_FACTOR%*iw:%UPSCALE_FACTOR%*ih:interp_algo=lanczos,unsharp_cuda=luma_amount=1.5,hwdownload,format=nv12"
        echo [%date% %time%] Using NVIDIA CUDA upscaling >> "%LOG_FILE%"
    ) else (
        SET "UPSCALE_FILTER=libplacebo=w=%UPSCALE_FACTOR%*iw:h=%UPSCALE_FACTOR%*ih:upscaler=ewa_lanczos"
        echo [%date% %time%] Using software upscaling >> "%LOG_FILE%"
    )
    
    REM Check if video filter already exists
    echo %ARGS% | findstr /C:"-vf" >nul
    if %errorlevel%==0 (
        REM Append to existing filter
        SET "ARGS=%ARGS:-vf =,-vf %UPSCALE_FILTER%,%"
    ) else (
        REM Add new filter before output
        SET "ARGS=%ARGS% -vf %UPSCALE_FILTER%"
    )
    
    echo [%date% %time%] Modified command: %ARGS% >> "%LOG_FILE%"
)

REM Execute real FFmpeg
echo [%date% %time%] Executing: %REAL_FFMPEG_PATH% %ARGS% >> "%LOG_FILE%"
"%REAL_FFMPEG_PATH%" %ARGS%
exit /b %errorlevel%
