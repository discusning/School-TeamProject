namespace ProtocolSeoul
{
    // 검사 모드 (보고서 9-5): 서류 칸 하나 → DB 칸 하나를 고르면 일치 / 불일치 / 관련 없음.
    // 두 칸의 종류(Field)가 같을 때만 비교한다 (보고서 9-6).
    // 대화 로그 (C061): 방문자 말풍선 하나와 서류·DB 칸 하나를 고르면, 그 칸 종류에 맞는 "말한 값"과 비교한다
    //   이름 칸 ↔ 말한 이름, 생년월일 칸 ↔ 말한 나이, 목적지 칸 ↔ 말한 목적지 (1주차 스토리 261122 판정 표 · 대화로그 PDF 6쪽 "방문자의 말과 서류가 다른지 대조")
    public static class Inspector
    {
        // 칸에 보이는 글자 (화면과 비교가 같은 값을 씀). 없는 칸은 null.
        public static string Value(Visitor v, Resident record, Source src, Field f)
        {
            switch (src)
            {
                case Source.TravelPass:
                    var p = v.Pass;
                    if (v.PassMissing) return null;
                    return f switch
                    {
                        Field.Name => p.Name,
                        Field.ResidentId => p.ResidentId,
                        Field.BirthDate => Fmt.Date(p.BirthDate),
                        Field.Address => p.Address,
                        Field.Photo => p.Photo.ToString(),
                        Field.Stamp => p.Stamp,
                        Field.IssueDate => Fmt.Date(p.IssueDate),
                        Field.CriminalRecord => p.CriminalRecord ?? "없음",
                        _ => null,
                    };
                case Source.EntryPermit:
                    var e = v.Permit;
                    if (e == null) return null;
                    return f switch
                    {
                        Field.Name => e.Name,
                        Field.Reason => e.Reason,
                        Field.Destination => e.Destination,
                        _ => null,
                    };
                case Source.Quarantine:
                    var c = v.Cert;
                    if (c == null) return null;
                    return f switch
                    {
                        Field.Name => c.Name,
                        Field.TestDate => Fmt.Date(c.TestDate),
                        Field.TestResult => c.Result,
                        Field.Stamp => c.Stamp,
                        _ => null,
                    };
                case Source.Database:
                    if (record == null) return null;
                    return f switch
                    {
                        Field.Name => record.Name,
                        Field.ResidentId => record.ResidentId,
                        Field.BirthDate => Fmt.Date(record.BirthDate),
                        Field.Address => record.Address,
                        Field.Photo => record.Face.ToString(),
                        Field.CriminalRecord => record.CriminalRecord ?? "없음",
                        _ => null,
                    };
                case Source.Booth:
                    return f == Field.Photo ? v.Face.ToString() : null;
                case Source.Log:
                    // 생년월일 칸과 비교할 때는 말한 나이 (숫자 글자)
                    return f switch
                    {
                        Field.Name => v.SaidName,
                        Field.BirthDate => v.SaidAge >= 0 ? v.SaidAge.ToString() : null,
                        Field.Destination => v.SaidDest,
                        _ => null,
                    };
            }
            return null;
        }

        // 생년월일 칸의 나이 (대화 로그의 말한 나이와 비교용). 없는 칸은 null
        static string AgeOf(Visitor v, Resident record, Source src)
        {
            if (src == Source.TravelPass && !v.PassMissing) return Portraits.Age(v.Pass.BirthDate).ToString();
            if (src == Source.Database && record != null) return Portraits.Age(record.BirthDate).ToString();
            return null;
        }

        public static CompareResult Compare(Visitor v, Resident record, Source srcA, Field fieldA, Source srcB, Field fieldB)
        {
            if (srcA == srcB) return CompareResult.Unrelated;
            // 대화 로그 말풍선은 상대 칸의 종류를 따름
            if (srcA == Source.Log || srcB == Source.Log)
            {
                var (os, of) = srcA == Source.Log ? (srcB, fieldB) : (srcA, fieldA);
                string said = Value(v, record, Source.Log, of);
                string doc = of == Field.BirthDate ? AgeOf(v, record, os) : Value(v, record, os, of);
                if (said == null || doc == null) return CompareResult.Unrelated;
                return said == doc ? CompareResult.Match : CompareResult.Mismatch;
            }
            if (fieldA != fieldB) return CompareResult.Unrelated;
            string x = Value(v, record, srcA, fieldA), y = Value(v, record, srcB, fieldB);
            if (x == null || y == null) return CompareResult.Unrelated;
            return x == y ? CompareResult.Match : CompareResult.Mismatch;
        }
    }
}
