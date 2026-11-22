using System;
using System.Collections.Generic;
using System.Linq;

namespace ProtocolSeoul
{
    // 방문자 초상 30장 목록 (디자인 노션 「방문자 npc」 캡션 기준). 그림 파일: Art/Portraits/Resources/Portraits/pNN.png
    // 주민은 나이(2050-03-01 기준)·성별(주민번호 뒷자리 첫 숫자: 홀수 남, 짝수 여)이 맞는 초상을 받는다.
    public static class Portraits
    {
        public static readonly DateTime BaseDate = new(2050, 3, 1);

        public struct Entry
        {
            public int Id, MinAge, MaxAge;
            public bool? Male;      // null = 캡션에 성별 없음 (70대 노인)
            public string Caption;
        }

        static Entry E(int id, int min, int max, bool? male, string cap) => new() { Id = id, MinAge = min, MaxAge = max, Male = male, Caption = cap };

        public static readonly Entry[] All =
        {
            E(1, 16, 19, true, "10대 후반 남자, 마른 체형"),
            E(2, 20, 29, true, "20대 남성, 키 크고 마름 (시선 회피, 비틀린 자세)"),
            E(3, 20, 23, false, "20대 초반 여성, 마른 체형"),
            E(4, 30, 39, true, "30대 남성, 근육질"),
            E(5, 30, 39, false, "30대 여성, 탄탄한 체형"),
            E(6, 50, 59, true, "50대 남성, 덩치 큰 체형"),
            E(7, 50, 59, false, "50대 여성, 다부진 형"),
            E(8, 40, 69, true, "60-40대 남성, 보통 체형, 안경"),
            E(9, 60, 69, true, "60대 남성, 뚱뚱한 체형"),
            E(10, 70, 79, null, "70대 노인, 구부정한 자세"),
            E(11, 60, 89, false, "80-60대 여성, 통통한 체형"),
            E(12, 20, 29, false, "20대 여성, 큰"),
            E(13, 24, 29, false, "20대 중후반 여성, 주근깨"),
            E(14, 30, 39, true, "30대 남성, 보통 키, 보통 체형"),
            E(15, 30, 39, false, "30대 어머니"),
            E(16, 40, 49, true, "40대 남성, 매우 큰 키, 수척함"),
            E(17, 40, 49, false, "40대 여성, 보통 키, 마름"),
            E(18, 40, 49, false, "40대 여성, 작은 키, 통통함"),
            E(19, 60, 69, true, "60대 남성, 매우 마름, 보통 키"),
            E(20, 5, 7, true, "6살 남자아이"),
            E(21, 90, 99, false, "90대 여성, 작은 키"),
            E(22, 8, 10, false, "9살 여자아이"),
            E(23, 10, 12, true, "11살 남자아이"),
            E(24, 20, 23, false, "20대 초반 여성, 마름, 작은 키"),
            E(25, 26, 29, true, "20대 후반 남성, 보통 키"),
            E(26, 26, 29, true, "20대 후반 남성, 덩치 큼, 키 큼"),
            E(27, 30, 33, false, "30대 초반 여성, 작은 키, 듬직한 체형"),
            E(28, 40, 49, false, "40대 여성, 보통 키"),
            E(29, 3, 5, false, "4살쯤 여자아이"),
            E(30, 53, 57, true, "50대 중반 남성, 보통 키"),
        };

        public static int Age(DateTime birth)
        {
            int a = BaseDate.Year - birth.Year;
            if (birth.AddYears(a) > BaseDate) a--;
            return a;
        }

        // 주민번호 "yyMMdd-g......" 의 g: 1·3 남, 2·4 여
        public static bool IsMale(string residentId) => residentId != null && residentId.Length > 7 && (residentId[7] - '0') % 2 == 1;

        public static Entry Get(int id) => All.FirstOrDefault(e => e.Id == id);

        // 나이·성별이 맞는 초상 후보 (없으면 같은 성별에서 나이가 가장 가까운 것)
        public static List<Entry> Candidates(int age, bool male)
        {
            var sex = All.Where(e => e.Male == null || e.Male == male).ToList();
            var fit = sex.Where(e => age >= e.MinAge && age <= e.MaxAge).ToList();
            if (fit.Count > 0) return fit;
            int Dist(Entry e) => age < e.MinAge ? e.MinAge - age : age - e.MaxAge;
            int best = sex.Min(Dist);
            return sex.Where(e => Dist(e) == best).ToList();
        }

        // pick: 같은 DB면 같은 초상이 나오게 정해진 수 (생성기의 난수에서 받음)
        public static int Pick(DateTime birth, string residentId, int pick)
        {
            var c = Candidates(Age(birth), IsMale(residentId));
            return c[Math.Abs(pick) % c.Count].Id;
        }
    }
}
