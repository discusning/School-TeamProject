using System;
using System.Collections.Generic;
using System.Linq;

namespace ProtocolSeoul
{
    // 주민등록 DB 한 줄 (보고서 8장 데이터 구조)
    public class Resident
    {
        public string Name, ResidentId, Address, CriminalRecord; // 범죄 이력 없으면 null
        public DateTime BirthDate;
        public Face Face;
    }

    // 임시 통행증 — 1차 선별소 발급. DB 항목 복사본 + 발급일 + 도장
    public class TravelPass
    {
        public string Name, ResidentId, Address, Stamp;
        public DateTime BirthDate, IssueDate;
        public Face Photo;
        public TravelPass Clone() => (TravelPass)MemberwiseClone();
    }

    // 방사능/바이러스 검역 소견서 — 검사일·결과·도장부터 (보고서 10장 제안)
    public class QuarantineCert
    {
        public string Name, Result, Stamp;
        public DateTime TestDate;
    }

    public class Visitor
    {
        public string Line;                 // 말풍선 대사
        public Face Face;                   // 부스에 실제로 보이는 얼굴
        public TravelPass Pass;
        public QuarantineCert Cert;         // 없을 수 있음
        public ErrorKind Planted;           // 생성기가 심은 오류 (QA용)
        public Verdict Correct;             // 정답 (Judge가 계산)
        public ErrorKind Reason;            // 정답 이유 (Judge가 찾은 오류)
    }

    public class ResidentDatabase
    {
        private readonly Dictionary<string, Resident> _byName = new();
        public IReadOnlyCollection<Resident> All => _byName.Values;
        public void Add(Resident r) => _byName.Add(r.Name, r);
        public bool Contains(string name) => _byName.ContainsKey(name);
        // 단말기는 이름으로 검색 (주민번호 한 자리 오류를 찾을 수 있게)
        public Resident FindByName(string name) => name != null && _byName.TryGetValue(name, out var r) ? r : null;

        static readonly string[] Surnames = { "김", "이", "박", "최", "정", "강", "조", "윤", "장", "임", "한", "오", "서", "신", "권", "황" };
        static readonly string[] Given = { "민준", "서연", "도윤", "하은", "시우", "지유", "예준", "서윤", "하준", "지민", "주원", "수아", "지호", "채원", "건우", "다은", "우진", "소율", "현우", "예린", "선우", "가은", "유찬", "나연" };
        public static readonly string[] Districts = { "강남", "서초", "송파", "용산", "마포", "종로" };
        static readonly string[] Crimes = { "절도 (2047)", "강도 (2048)", "배급품 위조 (2049)", "폭행 (2046)" };

        public static string MakeAddress(Random rng) => $"{Districts[rng.Next(Districts.Length)]} {(char)('A' + rng.Next(4))}-{rng.Next(1, 21)} 블록";

        public static string MakeId(DateTime birth, Random rng)
        {
            int gender = rng.Next(2);
            int g = birth.Year >= 2000 ? 3 + gender : 1 + gender;
            return $"{birth:yyMMdd}-{g}{rng.Next(0, 1000000):000000}";
        }

        // 2050년 서울 생존 구역 주민 count명 (같은 seed면 같은 DB)
        public static ResidentDatabase Generate(int seed, int count = 60)
        {
            var rng = new Random(seed);
            var db = new ResidentDatabase();
            while (db._byName.Count < count)
            {
                string name = Surnames[rng.Next(Surnames.Length)] + Given[rng.Next(Given.Length)];
                if (db.Contains(name)) continue;
                var birth = new DateTime(rng.Next(1985, 2032), rng.Next(1, 13), rng.Next(1, 29));
                db.Add(new Resident
                {
                    Name = name,
                    BirthDate = birth,
                    ResidentId = MakeId(birth, rng),
                    Address = MakeAddress(rng),
                    Face = new Face(rng.Next(4), rng.Next(4)),
                    CriminalRecord = rng.NextDouble() < 0.2 ? Crimes[rng.Next(Crimes.Length)] : null,
                });
            }
            return db;
        }

        public IEnumerable<Resident> Clean => All.Where(r => r.CriminalRecord == null);
        public IEnumerable<Resident> Criminals => All.Where(r => r.CriminalRecord != null);
    }
}
