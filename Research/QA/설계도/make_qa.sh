#!/usr/bin/env bash
# QA 보고서 + 안내서 만들기 (버전마다 2개 파일)
# 사용: bash make_qa.sh 261108
#  1) versions/{버전}.js 로 PDF 2개를 만든다 → Research/QA/{버전}/
#     - 프로토콜_QA_보고서_{버전}.pdf  (적는 칸만)
#     - 프로토콜_QA_안내서_{버전}.pdf  (적는 법)
#  2) 바탕화면 QA_보고서/{버전}/ 아래 1_보고서·2_안내서·3_받은 보고서 폴더를 만들고 복사한다.
set -e
V="$1"
[ -z "$V" ] && { echo "버전을 적어 주세요. 예: bash make_qa.sh 261108"; exit 1; }

HERE="$(cd "$(dirname "$0")" && pwd -W 2>/dev/null || pwd)"
[ -f "$HERE/versions/$V.js" ] || { echo "versions/$V.js 가 없습니다."; exit 1; }

CHROME="/c/Program Files/Google/Chrome/Application/chrome.exe"
OUT="$HERE/../$V"
SHARE="/c/Users/CHOI/OneDrive/Desktop/서울 생존 구역 보고서/QA_보고서/$V"
mkdir -p "$OUT" "$SHARE/1_보고서" "$SHARE/2_안내서" "$SHARE/3_받은 보고서"

for kind in report:보고서 guide:안내서; do
  f="${kind%%:*}"; name="${kind##*:}"
  "$CHROME" --headless=new --disable-gpu --no-pdf-header-footer --virtual-time-budget=2000 \
    --print-to-pdf="$OUT/프로토콜_QA_${name}_$V.pdf" "file:///$HERE/qa_$f.html?v=$V" 2>/dev/null
  cp "$OUT/프로토콜_QA_${name}_$V.pdf" "$SHARE/$( [ $f = report ] && echo 1_보고서 || echo 2_안내서 )/"
done
echo "완료: $SHARE"
ls -R "$SHARE"
