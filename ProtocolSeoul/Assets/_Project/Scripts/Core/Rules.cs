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

        // 입성 사유서 사유 (1주차 스토리 261122: "공격적이지 않으면 허용", 예: 가족 합류 · 의료 · 배정 근무).
        // 허용 3개는 원문 예시. 거부 사유 문구는 가정 (원문에 예시 없음)
        public static readonly string[] AllowedReasons = { "가족 합류", "의료", "배정 근무" };
        public static readonly string[] DeniedReasons = { "빼앗긴 물자를 되찾으러 감", "원수에게 복수하러 감", "요새 경비대와 싸우러 감" };
        public static bool ReasonAllowed(string reason) => Array.IndexOf(AllowedReasons, reason) >= 0;

        // 얼굴에 이상한 부분 (반전 · 늘림) — 외부인
        public static bool FaceAbnormal(Face f) => f.EyeOffset != 0 || f.Neck != 0;
        public static Face Normal(Face f) { f.EyeOffset = 0; f.Neck = 0; return f; }

        // 정답 판정 + 이유. 켜진 규칙만 본다. 여러 개 걸리면 먼저 걸린 것 하나를 이유로 씀.
        // 1주차 스토리 261122 "일차별 확인 항목과 판정": 얼굴 이상 → 사살(제거), 위조문서 판별(5일차) → 신고, 나머지 → 거부.
        public static (Verdict verdict, ErrorKind reason) Judge(Visitor v, DayConfig day, ResidentDatabase db)
        {
            if (day.Has(ErrorKind.OutsiderAppearance) && FaceAbnormal(v.Face)) return (Verdict.Kill, ErrorKind.OutsiderAppearance);
            if (v.PassMissing) return (Verdict.Deny, ErrorKind.PassMissing);
            var p = v.Pass;
            // 5일차~ 위조문서 판별: 선별소 도장 · 검역소견서 도장이 위조됨 (도장의 모양이 다르다, 도장이 없다) → 신고 (문서위조는 범죄)
            if (day.Has(ErrorKind.ForgedStamp) && p.Stamp != GenuineStamp) return (Verdict.Report, ErrorKind.ForgedStamp);
            if (day.Has(ErrorKind.CertStampForged) && v.Cert != null && v.Cert.Stamp != GenuineStamp) return (Verdict.Report, ErrorKind.CertStampForged);
            if (day.Has(ErrorKind.PassDateWrong) && p.IssueDate.Date != day.Today.Date) return (Verdict.Deny, ErrorKind.PassDateWrong);
            // 1일차 (DB 없음): 임시통행증 대조
            if (day.Has(ErrorKind.PhotoMismatch) && !Normal(v.Face).Equals(p.Photo)) return (Verdict.Deny, ErrorKind.PhotoMismatch);
            if (day.Has(ErrorKind.AgeMismatch) && v.SaidAge >= 0 && v.SaidAge != Portraits.Age(p.BirthDate)) return (Verdict.Deny, ErrorKind.AgeMismatch);
            if (day.Has(ErrorKind.SpokenNameMismatch) && v.SaidName != null && v.SaidName != p.Name) return (Verdict.Deny, ErrorKind.SpokenNameMismatch);
            if (day.Has(ErrorKind.NotSeoul) && !ResidentDatabase.IsSeoul(p.Address)) return (Verdict.Deny, ErrorKind.NotSeoul);
            // 2일차~ (DB 복구): DB 대조
            var r = db.FindByName(p.Name);
            if (day.Has(ErrorKind.NotInDatabase) && r == null) return (Verdict.Deny, ErrorKind.NotInDatabase);
            if (r != null)
            {
                if (day.Has(ErrorKind.IdDigitMismatch) && p.ResidentId != r.ResidentId) return (Verdict.Deny, ErrorKind.IdDigitMismatch);
                if (day.Has(ErrorKind.BirthMismatch) && p.BirthDate != r.BirthDate) return (Verdict.Deny, ErrorKind.BirthMismatch);
                if (day.Has(ErrorKind.AddressMismatch) && p.Address != r.Address) return (Verdict.Deny, ErrorKind.AddressMismatch);
                if (day.Has(ErrorKind.CriminalRecord) && r.CriminalRecord != null) return (Verdict.Deny, ErrorKind.CriminalRecord);
            }
            if (day.Has(ErrorKind.QuarantineMissing) && v.Cert == null) return (Verdict.Deny, ErrorKind.QuarantineMissing);
            if (day.Has(ErrorKind.QuarantineExpired) && v.Cert != null && CertExpired(v.Cert, day.Today)) return (Verdict.Deny, ErrorKind.QuarantineExpired);
            // 3일차~ 입성 사유서
            if (day.Has(ErrorKind.EntryMissing) && v.Permit == null) return (Verdict.Deny, ErrorKind.EntryMissing);
            if (v.Permit != null)
            {
                if (day.Has(ErrorKind.EntryNameMismatch) && (r == null || v.Permit.Name != r.Name)) return (Verdict.Deny, ErrorKind.EntryNameMismatch);
                if (day.Has(ErrorKind.EntryReasonDenied) && !ReasonAllowed(v.Permit.Reason)) return (Verdict.Deny, ErrorKind.EntryReasonDenied);
                if (day.Has(ErrorKind.EntryDestMismatch) && v.SaidDest != null && v.Permit.Destination != v.SaidDest) return (Verdict.Deny, ErrorKind.EntryDestMismatch);
            }
            return (Verdict.Pass, ErrorKind.None);
        }
    }
}
