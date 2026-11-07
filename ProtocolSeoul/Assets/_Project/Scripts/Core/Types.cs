using System;

namespace ProtocolSeoul
{
    // 판정 3택. 신고(Report)는 팀 결정 전이라 자리만 있음 (보고서 10장).
    public enum Verdict { Pass, Deny, Report }

    // 심을 수 있는 오류 = 그날 켜진 규칙. 보고서 8장 "오류 종류" 그대로.
    public enum ErrorKind
    {
        None,
        NotInDatabase,      // DB에 없는 이름
        IdDigitMismatch,    // 주민번호 한 자리 다름
        BirthMismatch,      // 생년월일 불일치
        AddressMismatch,    // 주소 불일치
        ForgedStamp,        // 선별소 도장 위조
        OutsiderAppearance, // 사진(DB)과 얼굴이 다름 = 외부인
        CriminalRecord,     // 범죄 이력 있음
        QuarantineMissing,  // 검역 소견서 없음
        QuarantineExpired,  // 검역 소견서 검사일 지남
    }

    // 화면에서 고를 수 있는 칸. 서류 칸과 DB 칸에 같은 종류가 붙어 있어야 비교됨 (보고서 9-6).
    public enum Field { Name, ResidentId, BirthDate, Address, Photo, Stamp, IssueDate, TestDate, TestResult, CriminalRecord }

    // 칸이 어디에 있는지
    public enum Source { TravelPass, Quarantine, Database, Booth }

    public enum CompareResult { Match, Mismatch, Unrelated }

    // 얼굴 (임시 그림: 도형). 외부인은 눈 위치나 목 길이가 다름 (That's Not My Neighbor 방식).
    [Serializable]
    public struct Face : IEquatable<Face>
    {
        public int Skin, Hair, EyeOffset, Neck;
        public Face(int skin, int hair, int eyeOffset = 0, int neck = 0) { Skin = skin; Hair = hair; EyeOffset = eyeOffset; Neck = neck; }
        public bool Equals(Face o) => Skin == o.Skin && Hair == o.Hair && EyeOffset == o.EyeOffset && Neck == o.Neck;
        public override bool Equals(object obj) => obj is Face f && Equals(f);
        public override int GetHashCode() => (Skin, Hair, EyeOffset, Neck).GetHashCode();
        public override string ToString() => $"F{Skin}{Hair}{EyeOffset}{Neck}";
    }

    public static class Fmt
    {
        public static string Date(DateTime d) => d.ToString("yyyy.MM.dd");
        public static string FieldName(Field f) => f switch
        {
            Field.Name => "이름", Field.ResidentId => "주민번호", Field.BirthDate => "생년월일", Field.Address => "주소",
            Field.Photo => "사진", Field.Stamp => "도장", Field.IssueDate => "발급일", Field.TestDate => "검사일",
            Field.TestResult => "검사 결과", Field.CriminalRecord => "범죄 이력", _ => f.ToString(),
        };
        public static string ErrorText(ErrorKind e) => e switch
        {
            ErrorKind.None => "문제 없음",
            ErrorKind.NotInDatabase => "주민등록 DB에 없는 사람",
            ErrorKind.IdDigitMismatch => "주민번호가 DB와 다름",
            ErrorKind.BirthMismatch => "생년월일이 DB와 다름",
            ErrorKind.AddressMismatch => "주소가 DB와 다름",
            ErrorKind.ForgedStamp => "선별소 도장이 위조됨",
            ErrorKind.OutsiderAppearance => "얼굴이 DB 사진과 다름 (외부인)",
            ErrorKind.CriminalRecord => "범죄 이력이 있음",
            ErrorKind.QuarantineMissing => "검역 소견서가 없음",
            ErrorKind.QuarantineExpired => "검역 소견서 검사일이 지남",
            _ => e.ToString(),
        };
    }
}
