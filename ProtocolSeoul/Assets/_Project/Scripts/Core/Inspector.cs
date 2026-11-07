namespace ProtocolSeoul
{
    // 검사 모드 (보고서 9-5): 서류 칸 하나 → DB 칸 하나를 고르면 일치 / 불일치 / 관련 없음.
    // 두 칸의 종류(Field)가 같을 때만 비교한다 (보고서 9-6).
    public static class Inspector
    {
        // 칸에 보이는 글자 (화면과 비교가 같은 값을 씀). 없는 칸은 null.
        public static string Value(Visitor v, Resident record, Source src, Field f)
        {
            switch (src)
            {
                case Source.TravelPass:
                    var p = v.Pass;
                    return f switch
                    {
                        Field.Name => p.Name,
                        Field.ResidentId => p.ResidentId,
                        Field.BirthDate => Fmt.Date(p.BirthDate),
                        Field.Address => p.Address,
                        Field.Photo => p.Photo.ToString(),
                        Field.Stamp => p.Stamp,
                        Field.IssueDate => Fmt.Date(p.IssueDate),
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
            }
            return null;
        }

        public static CompareResult Compare(Visitor v, Resident record, Source srcA, Field fieldA, Source srcB, Field fieldB)
        {
            if (fieldA != fieldB || srcA == srcB) return CompareResult.Unrelated;
            string x = Value(v, record, srcA, fieldA), y = Value(v, record, srcB, fieldB);
            if (x == null || y == null) return CompareResult.Unrelated;
            return x == y ? CompareResult.Match : CompareResult.Mismatch;
        }
    }
}
