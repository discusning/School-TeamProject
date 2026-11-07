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
            var db = new[] { ErrorKind.NotInDatabase, ErrorKind.IdDigitMismatch, ErrorKind.BirthMismatch, ErrorKind.AddressMismatch };
            var d2 = new List<ErrorKind>(db) { ErrorKind.OutsiderAppearance, ErrorKind.ForgedStamp };
            var d3 = new List<ErrorKind>(d2) { ErrorKind.QuarantineMissing, ErrorKind.QuarantineExpired, ErrorKind.CriminalRecord };
            return new List<DayConfig>
            {
                Make(1, 6, 0.4f, "통행증 ↔ 주민등록 DB 대조",
                    "제2검문소 근무 첫날.\n임시 통행증의 이름·주민번호·생년월일·주소를 주민등록 DB와 대조하라.\n하나라도 다르거나 DB에 없으면 거부.", db),
                Make(2, 7, 0.45f, "DB 대조 + 얼굴 확인 + 도장 확인",
                    "어젯밤 외부인이 섞여 들어왔다는 보고가 있다.\n부스의 얼굴이 DB 사진과 다르면 거부하라.\n정상 도장은 「" + ProtocolSeoul.Rules.GenuineStamp + "」 뿐이다.", d2.ToArray()),
                Make(3, 8, 0.5f, "+ 검역 소견서 (검사일 3일 이내) · 범죄 이력 거부",
                    "오늘부터 방사능/바이러스 검역 소견서가 필수다.\n검사일이 오늘 기준 3일 이내여야 한다.\nDB에 범죄 이력이 있는 자도 거부하라.", d3.ToArray()),
            };
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
