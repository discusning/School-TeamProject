using System;
using System.Collections.Generic;
using System.Globalization;
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

        // 날짜 글자는 PC 지역 설정과 상관없이 같은 값으로 읽음 (C060)
        public DateTime Today => DateTime.ParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        public bool Has(ErrorKind k) => Rules.Contains(k);

        // 화면 왼쪽 위 날짜 = 임시통행증 정상 발급일 (C060, 사용자 2026-11-23 "2일차-2050.03.02" 형식)
        public string DayLabel => $"{Day}일차-{Fmt.Date(Today)}";

        // 이날 나오는 자료 (C058 항목 3 표. 검역 소견서는 3차 회의록 "2일차부터 추가"를 따름, C060)
        // 주민등록 DB: 2일차~ (1일차 DB 없음)
        public bool HasDatabase => Has(ErrorKind.NotInDatabase) || Has(ErrorKind.IdDigitMismatch) || Has(ErrorKind.AddressMismatch) || Has(ErrorKind.BirthMismatch);
        // 검역 소견서: 2일차~
        public bool HasCert => Has(ErrorKind.QuarantineMissing) || Has(ErrorKind.QuarantineExpired);
        // 입성 사유서: 3일차~
        public bool HasPermit => Has(ErrorKind.EntryMissing) || Has(ErrorKind.EntryNameMismatch) || Has(ErrorKind.EntryReasonDenied) || Has(ErrorKind.EntryDestMismatch);
        // 위조 판별: 5일차~
        public bool HasForgery => Has(ErrorKind.ForgedStamp) || Has(ErrorKind.CertStampForged);

        // 임시통행증 글줄. 생년월일은 모든 일차 (C060 가정: 2일차~ 목록에 없는 것을 뺀 근거로 보지 않음). 범죄기록은 2일차~ (스토리 261122)
        public Field[] PassFields => HasDatabase
            ? new[] { Field.Name, Field.ResidentId, Field.BirthDate, Field.Address, Field.CriminalRecord, Field.IssueDate }
            : new[] { Field.Name, Field.ResidentId, Field.BirthDate, Field.Address, Field.IssueDate };

        // 기본 5일 (에셋이 없을 때·테스트용). 규칙이 하루에 하나씩 늘어남 (페이퍼 플리즈 방식, 보고서 "날짜별 규칙 추가")
        // 5일차 안내 글: 스토리 261122 5일차 글머리 그대로 + 마지막 줄은 3·4일차와 같은 가정 줄
        public const string Day5Briefing = "위조서류를 발견한 정부는 DB에 위조문서 규칙을 추가\n신고 버튼 활성화 (문서위조는 범죄)\n임시통행증 (이름, 얼굴, 주민번호, 주소, 범죄기록)\n입성 사유서 (이름, 사유)\n3일차 입성사유 허용 : 공격적이지 않으면 허용\n위조문서 판별\n검역 소견서 필수 (검사일 3일 이내) · DB에 범죄 이력이 있으면 거부";

        public static List<DayConfig> Defaults()
        {
            // Resources/Days/Day1~5.asset과 같은 규칙 (1주차 스토리 261122 "일차별 확인 항목과 판정")
            var d1 = new[] { ErrorKind.OutsiderAppearance, ErrorKind.PassMissing, ErrorKind.PassDateWrong, ErrorKind.AgeMismatch, ErrorKind.PhotoMismatch, ErrorKind.SpokenNameMismatch, ErrorKind.NotSeoul };
            // 검역 소견서는 2일차부터 (3차 회의록 261117 "2일차부터 검역 소견서 추가", C060)
            var d2 = new List<ErrorKind> { ErrorKind.NotInDatabase, ErrorKind.IdDigitMismatch, ErrorKind.AddressMismatch, ErrorKind.OutsiderAppearance, ErrorKind.PassMissing, ErrorKind.PassDateWrong, ErrorKind.QuarantineMissing, ErrorKind.QuarantineExpired };
            var d3 = new List<ErrorKind>(d2) { ErrorKind.CriminalRecord, ErrorKind.EntryMissing, ErrorKind.EntryNameMismatch, ErrorKind.EntryReasonDenied, ErrorKind.EntryDestMismatch };
            var d5 = new List<ErrorKind>(d3) { ErrorKind.ForgedStamp, ErrorKind.CertStampForged };
            var days = new List<DayConfig>
            {
                Make(1, 6, 0.4f, "얼굴 · 임시통행증 확인 (주민등록 DB 없음)",
                    "통과, 거부, 제거 버튼 활성화\n주민등록 DB 없음\n얼굴과 임시통행증을 보고 판단", d1),
                Make(2, 7, 0.45f, "+ DB 대조 (주민번호 · 이름 · 주소) · 모든 도시 사람 · 검역 소견서",
                    "주민등록 DB 복구\n임시통행증 (이름, 얼굴, 주민번호, 주소, 범죄기록)\nDB복구로 모든 도시 사람 받을 수 있음.\n검역 소견서 필수 (검사일 3일 이내)", d2.ToArray()),
                Make(3, 8, 0.5f, "+ 입성 사유서 · 범죄 이력 거부",
                    "주민등록 DB 정상 가동\n임시통행증 (이름, 얼굴, 주민번호, 주소, 범죄기록)\n입성 사유서 (이름, 사유)\n3일차 입성사유 허용 : 공격적이지 않으면 허용\n검역 소견서 필수 (검사일 3일 이내) · DB에 범죄 이력이 있으면 거부", d3.ToArray()),
                // 4일차: 확인 항목은 3일차와 같음 (스토리 261122 "일차별 확인 항목과 판정" 표 4일차 열). 인원·오류 비율은 3일차와 같게 (가정)
                Make(4, 8, 0.5f, "3일차와 같음 (입성 사유서 · 범죄 이력 거부)",
                    "주민등록 DB 정상 가동\n임시통행증 (이름, 얼굴, 주민번호, 주소, 범죄기록)\n입성 사유서 (이름, 사유)\n3일차 입성사유 허용 : 공격적이지 않으면 허용\n검역 소견서 필수 (검사일 3일 이내) · DB에 범죄 이력이 있으면 거부", d3.ToArray()),
                // 5일차: 4일차 + 위조문서 판별 (선별소 도장 · 검역소견서 도장 → 신고), 신고 버튼 활성화 (스토리 261122 5일차 · 위조문서 판별 표). 인원·오류 비율은 4일차와 같게 (가정)
                Make(5, 8, 0.5f, "+ 위조문서 판별 (선별소 · 검역소견서 도장) → 신고",
                    Day5Briefing, d5.ToArray()),
            };
            days[2].ScriptedVisitors = new List<string> { "kang", "jin" };
            days[3].ScriptedVisitors = new List<string> { VisitorGenerator.KangForged };
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
