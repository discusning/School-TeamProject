using System;

namespace ProtocolSeoul
{
    // 판정. 신고(Report)는 5일차부터라 자리만 있음. 제거(Kill, 화면 글자 "제거")는 1일차부터 (1주차 스토리 261122).
    public enum Verdict { Pass, Deny, Report, Kill }

    // 심을 수 있는 오류 = 그날 켜진 규칙. 보고서 8장 "오류 종류" 그대로.
    public enum ErrorKind
    {
        None,
        NotInDatabase,      // DB에 없는 이름
        IdDigitMismatch,    // 주민번호 한 자리 다름
        BirthMismatch,      // 생년월일 불일치
        AddressMismatch,    // 주소 불일치
        ForgedStamp,        // 선별소 도장 위조
        OutsiderAppearance, // 얼굴에 이상한 부분 (반전·늘림) = 외부인 → 제거
        CriminalRecord,     // 범죄 이력 있음
        QuarantineMissing,  // 검역 소견서 없음
        QuarantineExpired,  // 검역 소견서 검사일 지남
        // 1주차 스토리 261122 "일차별 확인 항목과 판정" (에셋에 숫자로 저장되므로 뒤에만 붙임)
        PassMissing,        // 임시통행증이 없음
        PassDateWrong,      // 임시통행증 날짜가 오늘이 아님 (받은 날만 입성 가능)
        AgeMismatch,        // 말한 나이 ≠ 통행증 나이 (1일차)
        PhotoMismatch,      // 실제 얼굴 ≠ 통행증 사진 (1일차)
        SpokenNameMismatch, // 말한 이름 ≠ 통행증 이름 (1일차)
        NotSeoul,           // 서울 사람이 아님 (1일차)
        EntryMissing,       // 입성 사유서 없음 (3일차~)
        EntryNameMismatch,  // 사유서 이름 ≠ DB
        EntryReasonDenied,  // 허용되지 않는 입성 사유
        EntryDestMismatch,  // 사유서 목적지 ≠ 말한 목적지 (대화 로그)
    }

    // 화면에서 고를 수 있는 칸. 서류 칸과 DB 칸에 같은 종류가 붙어 있어야 비교됨 (보고서 9-6).
    public enum Field { Name, ResidentId, BirthDate, Address, Photo, Stamp, IssueDate, TestDate, TestResult, CriminalRecord, Reason, Destination }

    // 칸이 어디에 있는지
    public enum Source { TravelPass, Quarantine, Database, Booth, EntryPermit }

    public enum CompareResult { Match, Mismatch, Unrelated }

    // 얼굴. Portrait = 초상 그림 번호 (1~30, Portraits.cs). 0이면 도형 얼굴.
    // 외부인은 같은 초상을 좌우 반전(EyeOffset ≠ 0)하거나 세로로 늘림(Neck = 1) (That's Not My Neighbor 방식).
    [Serializable]
    public struct Face : IEquatable<Face>
    {
        public int Skin, Hair, EyeOffset, Neck, Portrait;
        public Face(int skin, int hair, int eyeOffset = 0, int neck = 0, int portrait = 0) { Skin = skin; Hair = hair; EyeOffset = eyeOffset; Neck = neck; Portrait = portrait; }
        public bool Equals(Face o) => Skin == o.Skin && Hair == o.Hair && EyeOffset == o.EyeOffset && Neck == o.Neck && Portrait == o.Portrait;
        public override bool Equals(object obj) => obj is Face f && Equals(f);
        public override int GetHashCode() => (Skin, Hair, EyeOffset, Neck, Portrait).GetHashCode();
        public override string ToString() => $"F{Skin}{Hair}{EyeOffset}{Neck}P{Portrait}";
    }

    public static class Fmt
    {
        public static string Date(DateTime d) => d.ToString("yyyy.MM.dd");
        public static string FieldName(Field f) => f switch
        {
            Field.Name => "이름", Field.ResidentId => "주민번호", Field.BirthDate => "생년월일", Field.Address => "주소",
            Field.Photo => "사진", Field.Stamp => "도장", Field.IssueDate => "발급일", Field.TestDate => "검사일",
            Field.TestResult => "검사 결과", Field.CriminalRecord => "범죄기록", Field.Reason => "사유", Field.Destination => "목적지", _ => f.ToString(),
        };
        public static string ErrorText(ErrorKind e) => e switch
        {
            ErrorKind.None => "문제 없음",
            ErrorKind.NotInDatabase => "주민등록 DB에 없는 사람",
            ErrorKind.IdDigitMismatch => "주민번호가 DB와 다름",
            ErrorKind.BirthMismatch => "생년월일이 DB와 다름",
            ErrorKind.AddressMismatch => "주소가 DB와 다름",
            ErrorKind.ForgedStamp => "선별소 도장이 위조됨",
            ErrorKind.OutsiderAppearance => "얼굴에 이상한 부분이 있음 (외부인)",
            ErrorKind.CriminalRecord => "범죄 이력이 있음",
            ErrorKind.QuarantineMissing => "검역 소견서가 없음",
            ErrorKind.QuarantineExpired => "검역 소견서 검사일이 지남",
            ErrorKind.PassMissing => "임시통행증이 없음",
            ErrorKind.PassDateWrong => "임시통행증의 날짜가 잘못됨 (받은 날만 입성 가능)",
            ErrorKind.AgeMismatch => "말한 나이가 임시통행증의 나이와 다름",
            ErrorKind.PhotoMismatch => "실제 얼굴이 임시통행증의 사진과 다름",
            ErrorKind.SpokenNameMismatch => "말한 이름이 임시통행증의 이름과 다름",
            ErrorKind.NotSeoul => "서울 사람이 아님",
            ErrorKind.EntryMissing => "입성 사유서가 없음",
            ErrorKind.EntryNameMismatch => "사유서의 이름이 DB와 다름",
            ErrorKind.EntryReasonDenied => "허용되지 않는 입성 사유",
            ErrorKind.EntryDestMismatch => "사유서의 목적지가 말한 목적지와 다름",
            _ => e.ToString(),
        };
    }
}
