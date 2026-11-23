using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProtocolSeoul
{
    // 하루 설정 — 기획 팀원이 코드 없이 숫자를 바꿀 수 있게 에셋으로 둔다 (보고서 8장 설계 원칙).
    // 에셋 위치: Assets/_Project/Resources/Days/Day1.asset ... (Unity 메뉴 Protocol Seoul > Setup Project가 처음 한 번 만듦)
    [CreateAssetMenu(menuName = "Protocol Seoul/Day Config", fileName = "Day")]
    public class DayConfig : ScriptableObject
    {
        [Tooltip("몇 일차")] public int Day = 1;
        [Tooltip("게임 속 날짜 (yyyy-MM-dd)")] public string Date = "2050-03-01";
        [Tooltip("하루 방문자 수 (5~9)")] [Range(1, 15)] public int VisitorCount = 6;
        [Tooltip("방문자 1명에게 오류를 심을 확률")] [Range(0f, 1f)] public float ErrorRate = 0.4f;
        [Tooltip("이날 켜진 규칙 (= 심을 수 있는 오류)")] public List<ErrorKind> Rules = new();
        [Tooltip("하루 시작 때 보여 줄 오늘의 지시")] [TextArea(2, 5)] public string Briefing = "";
        [Tooltip("위쪽에 짧게 보일 오늘의 규칙")] public string RuleShort = "";
        [Tooltip("대본 인물 자리 (스토리 확정 뒤 사용, 지금은 비움)")] public List<string> ScriptedVisitors = new();

        public DateTime Today => DateTime.Parse(Date);
        public bool Has(ErrorKind k) => Rules.Contains(k);

        // 기본 3일 (에셋이 없을 때·테스트용). 규칙이 하루에 하나씩 늘어남 (페이퍼 플리즈 방식, 보고서 "날짜별 규칙 추가")
        public static List<DayConfig> Defaults()
        {
            // Resources/Days/Day1~3.asset과 같은 규칙 (1주차 스토리 261122 "일차별 확인 항목과 판정")
            var d1 = new[] { ErrorKind.OutsiderAppearance, ErrorKind.PassMissing, ErrorKind.PassDateWrong, ErrorKind.AgeMismatch, ErrorKind.PhotoMismatch, ErrorKind.SpokenNameMismatch, ErrorKind.NotSeoul };
            var d2 = new List<ErrorKind> { ErrorKind.NotInDatabase, ErrorKind.IdDigitMismatch, ErrorKind.AddressMismatch, ErrorKind.OutsiderAppearance, ErrorKind.PassMissing, ErrorKind.PassDateWrong };
            var d3 = new List<ErrorKind>(d2) { ErrorKind.QuarantineMissing, ErrorKind.QuarantineExpired, ErrorKind.CriminalRecord, ErrorKind.EntryMissing, ErrorKind.EntryNameMismatch, ErrorKind.EntryReasonDenied, ErrorKind.EntryDestMismatch };
            var days = new List<DayConfig>
            {
                Make(1, 6, 0.4f, "얼굴 · 임시통행증 확인 (주민등록 DB 없음)",
                    "통과, 거부, 제거 버튼 활성화\n주민등록 DB 없음\n얼굴과 임시통행증을 보고 판단", d1),
                Make(2, 7, 0.45f, "+ DB 대조 (주민번호 · 이름 · 주소) · 모든 도시 사람",
                    "주민등록 DB 복구\n임시통행증 (이름, 얼굴, 주민번호, 주소, 범죄기록)\nDB복구로 모든 도시 사람 받을 수 있음.", d2.ToArray()),
                Make(3, 8, 0.5f, "+ 입성 사유서 · 검역 소견서 · 범죄 이력 거부",
                    "주민등록 DB 정상 가동\n임시통행증 (이름, 얼굴, 주민번호, 주소, 범죄기록)\n입성 사유서 (이름, 사유)\n3일차 입성사유 허용 : 공격적이지 않으면 허용\n검역 소견서 필수 (검사일 3일 이내) · DB에 범죄 이력이 있으면 거부", d3.ToArray()),
            };
            days[2].ScriptedVisitors = new List<string> { "kang", "jin" };
            return days;
        }

        public static DayConfig Make(int day, int count, float rate, string ruleShort, string briefing, params ErrorKind[] rules)
        {
            var c = CreateInstance<DayConfig>();
            c.name = $"Day{day}";
            c.Day = day;
            c.Date = $"2050-03-{day:00}";
            c.VisitorCount = count;
            c.ErrorRate = rate;
            c.Rules = new List<ErrorKind>(rules);
            c.RuleShort = ruleShort;
            c.Briefing = briefing;
            return c;
        }
    }
}
