#!/bin/bash
# Build one technique's scene from code, render it frame by frame in batch mode and encode an MP4.
# usage: tools/capture.sh <technique> <Builder.Method> [frames=720] [fps=30] [w=1920] [h=1080] [max_kb=3600]
#   e.g. tools/capture.sh smoke-lighting GameTech.Posts.SmokeScene.Build
# Needs: Unity 6000.3.28f1 (set UNITY=/path/to/Unity if it is not found) and ffmpeg on PATH (or FFMPEG=...).
# Output: Recordings/<technique>/video.mp4 and image.jpg. Frames are deleted after encoding.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
T="$1"; BUILDER="$2"; FRAMES="${3:-720}"; FPS="${4:-30}"; W="${5:-1920}"; H="${6:-1080}"; MAXKB="${7:-3600}"
if [ -z "${UNITY:-}" ]; then
  UNITY="$(ls -d /Applications/Unity/Hub/Editor/6000.3.*/Unity.app 2>/dev/null | tail -1)/Contents/MacOS/Unity"
fi
FF="${FFMPEG:-ffmpeg}"
FR="$ROOT/Recordings/.frames/$T"; OUT="$ROOT/Recordings/$T"; LOG="$ROOT/Recordings/$T.log"
rm -rf "$FR"; mkdir -p "$FR" "$OUT"
"$UNITY" -batchmode -projectPath "$ROOT" -logFile "$LOG" \
  -executeMethod GameTech.BatchCapture.Run -gtCapture -gtScene "Assets/Techniques/$T/Main.unity" \
  -gtOut "$FR" -gtFrames "$FRAMES" -gtFps "$FPS" -gtW "$W" -gtH "$H" -gtBuilder "$BUILDER"
N=$(ls "$FR" 2>/dev/null | wc -l | tr -d ' ')
echo "frames: $N/$FRAMES"
if [ "$N" -lt "$FRAMES" ]; then grep -E "error CS|Exception|\[gt\]" "$LOG" | tail -25; exit 1; fi
EXT=jpg; [ -f "$FR/f_0000.png" ] && EXT=png
BR=$(( MAXKB * 8 * FPS / FRAMES ))
VF="scale=in_range=pc:out_range=tv:out_color_matrix=bt709,format=yuv420p"
"$FF" -y -loglevel error -framerate "$FPS" -i "$FR/f_%04d.$EXT" -c:v libx264 -preset slow -b:v "${BR}k" \
  -maxrate "$((BR*3/2))k" -bufsize "$((BR*2))k" -vf "$VF" -pix_fmt yuv420p -movflags +faststart -an "$OUT/video.mp4"
"$FF" -y -loglevel error -i "$FR/f_$(printf %04d $((FRAMES/2))).$EXT" -q:v 2 "$OUT/image.jpg"
rm -rf "$FR"
echo "done: $OUT"
