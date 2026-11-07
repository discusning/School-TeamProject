using System;

namespace ProtocolSeoul
{
    // 판정 기준은 이 파일 한 곳에서만 계산한다 (보고서 8장 설계 원칙). 생성기·화면·테스트 모두 여기를 부른다.
    public static class Rules
    {
        public const string GenuineStamp = "제1선별소 확인";
        public static readonly string[] ForgedStamps = { "제1선별쇼 확인", "제I선별소 확인", "제1선발소 확인", "제7선별소 확인" };
        public const int QuarantineValidDays = 3;
        public const string NegativeResult = "음성";

        public static bool CertExpired(QuarantineCert c, DateTime today) => (today - c.TestDate).TotalDays > QuarantineValidDays;

        // 정답 판정 + 이유. 켜진 규칙만 본다. 여러 개 걸리면 먼저 걸린 것 하나를 이유로 씀.
        // 외부인도 지금은 "거부" — 신고 버튼은 팀 결정 뒤 (보고서 10장).
        public static (Verdict verdict, ErrorKind reason) Judge(Visitor v, DayConfig day, ResidentDatabase db)
        {
            var p = v.Pass;
            var r = db.FindByName(p.Name);
            if (r == null) return (Verdict.Deny, ErrorKind.NotInDatabase);
            if (day.Has(ErrorKind.IdDigitMismatch) && p.ResidentId != r.ResidentId) return (Verdict.Deny, ErrorKind.IdDigitMismatch);
            if (day.Has(ErrorKind.BirthMismatch) && p.BirthDate != r.BirthDate) return (Verdict.Deny, ErrorKind.BirthMismatch);
            if (day.Has(ErrorKind.AddressMismatch) && p.Address != r.Address) return (Verdict.Deny, ErrorKind.AddressMismatch);
            if (day.Has(ErrorKind.ForgedStamp) && p.Stamp != GenuineStamp) return (Verdict.Deny, ErrorKind.ForgedStamp);
            if (day.Has(ErrorKind.OutsiderAppearance) && !v.Face.Equals(r.Face)) return (Verdict.Deny, ErrorKind.OutsiderAppearance);
            if (day.Has(ErrorKind.CriminalRecord) && r.CriminalRecord != null) return (Verdict.Deny, ErrorKind.CriminalRecord);
            if (day.Has(ErrorKind.QuarantineMissing) && v.Cert == null) return (Verdict.Deny, ErrorKind.QuarantineMissing);
            if (day.Has(ErrorKind.QuarantineExpired) && v.Cert != null && CertExpired(v.Cert, day.Today)) return (Verdict.Deny, ErrorKind.QuarantineExpired);
            return (Verdict.Pass, ErrorKind.None);
        }
    }
}
