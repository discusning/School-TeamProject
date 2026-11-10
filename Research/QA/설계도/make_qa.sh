#!/usr/bin/env bash
# QA 보고서 빈 양식 만들기
# 사용: bash make_qa.sh 261108
#  1) versions/{버전}.js 로 PDF를 만든다 → Research/QA/프로토콜_QA_보고서_{버전}.pdf
#  2) 바탕화면 공유 폴더에 {버전}/받은 보고서/ 를 만들고 빈 양식을 복사한다.
set -e
V="$1"
[ -z "$V" ] && { echo "버전을 적어 주세요. 예: bash make_qa.sh 261108"; exit 1; }

HERE="$(cd "$(dirname "$0")" && pwd -W 2>/dev/null || pwd)"
[ -f "$HERE/versions/$V.js" ] || { echo "versions/$V.js 가 없습니다."; exit 1; }

CHROME="/c/Program Files/Google/Chrome/Application/chrome.exe"
OUT="$HERE/../프로토콜_QA_보고서_$V.pdf"
SHARE="/c/Users/CHOI/OneDrive/Desktop/서울 생존 구역 보고서/QA_보고서/$V"

"$CHROME" --headless=new --disable-gpu --no-pdf-header-footer --virtual-time-budget=2000 \
  --print-to-pdf="$OUT" "file:///$HERE/qa_template.html?v=$V" 2>/dev/null
mkdir -p "$SHARE/받은 보고서"
cp "$OUT" "$SHARE/"
echo "완료: $SHARE/프로토콜_QA_보고서_$V.pdf"
