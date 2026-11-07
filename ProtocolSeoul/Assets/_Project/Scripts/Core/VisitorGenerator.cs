using System;
using System.Collections.Generic;
using System.Linq;

namespace ProtocolSeoul
{
    // 방문자 생성 (페이퍼 플리즈 방식, 보고서 8장): DB에서 주민 1명을 뽑아 서류를 정확히 복사
    // → 일정 확률로 오류 하나를 심음 → 정답은 Rules.Judge가 계산.
    public class VisitorGenerator
    {
        private readonly Random _rng;
        private readonly ResidentDatabase _db;

        static readonly string[] Lines =
        {
            "통행증 여기 있습니다.", "강남 쪽 배급소에 가야 해요.", "오늘 안에 들어가야 합니다.", "선별소에서 바로 왔어요.",
            "가족이 안에서 기다리고 있어요.", "줄이 너무 길었네요… 여기요.", "검사는 다 받았습니다.", "빨리 좀 부탁드려요.",
        };
        static readonly string[] OutsiderLines = { "안녕하세요. 저는 이 구역 주민입니다.", "통행증. 여기. 있습니다.", "들여보내 주세요. 집에… 가야 해요." };
        static readonly string[] Strangers = { "백도현", "남궁별", "마서진", "탁지후", "여은솔", "편하람" };

        public VisitorGenerator(ResidentDatabase db, int seed) { _db = db; _rng = new Random(seed); }

        public List<Visitor> GenerateDay(DayConfig day)
        {
            var list = new List<Visitor>();
            var used = new HashSet<string>();
            for (int i = 0; i < day.VisitorCount; i++)
            {
                var kind = ErrorKind.None;
                if (day.Rules.Count > 0 && _rng.NextDouble() < day.ErrorRate) kind = day.Rules[_rng.Next(day.Rules.Count)];
                list.Add(Generate(day, kind, used));
            }
            // 하루에 오류가 하나도 없으면 연습이 안 되므로 마지막 1명에게 심음
            if (day.Rules.Count > 0 && list.All(v => v.Planted == ErrorKind.None))
                list[list.Count - 1] = Generate(day, day.Rules[_rng.Next(day.Rules.Count)], used);
            return list;
        }

        public Visitor Generate(DayConfig day, ErrorKind kind, HashSet<string> used = null)
        {
            used ??= new HashSet<string>();
            var pool = (kind == ErrorKind.CriminalRecord ? _db.Criminals : _db.Clean).Where(r => !used.Contains(r.Name)).ToList();
            if (pool.Count == 0) pool = _db.All.ToList();
            var r = pool[_rng.Next(pool.Count)];
            used.Add(r.Name);

            var v = new Visitor
            {
                Line = Lines[_rng.Next(Lines.Length)],
                Face = r.Face,
                Pass = new TravelPass
                {
                    Name = r.Name, ResidentId = r.ResidentId, BirthDate = r.BirthDate, Address = r.Address,
                    Photo = r.Face, IssueDate = day.Today.AddDays(-_rng.Next(0, 3)), Stamp = Rules.GenuineStamp,
                },
                Planted = kind,
            };
            if (day.Has(ErrorKind.QuarantineMissing) || day.Has(ErrorKind.QuarantineExpired))
                v.Cert = new QuarantineCert
                {
                    Name = r.Name, Result = Rules.NegativeResult, Stamp = Rules.GenuineStamp,
                    TestDate = day.Today.AddDays(-_rng.Next(0, Rules.QuarantineValidDays + 1)),
                };

            Plant(v, kind, day, used);
            (v.Correct, v.Reason) = Rules.Judge(v, day, _db);
            return v;
        }

        private void Plant(Visitor v, ErrorKind kind, DayConfig day, HashSet<string> used)
        {
            var p = v.Pass;
            switch (kind)
            {
                case ErrorKind.NotInDatabase:
                    var free = Strangers.Where(n => !_db.Contains(n) && !used.Contains(n)).ToList();
                    p.Name = free.Count > 0 ? free[_rng.Next(free.Count)] : "무명";
                    used.Add(p.Name);
                    if (v.Cert != null) v.Cert.Name = p.Name;
                    break;
                case ErrorKind.IdDigitMismatch: // 뒷자리 한 자리만 바꿈 (앞 6자리 = 생년월일은 그대로)
                    var chars = p.ResidentId.ToCharArray();
                    int at = 8 + _rng.Next(6);
                    chars[at] = (char)('0' + (chars[at] - '0' + 1 + _rng.Next(8)) % 10);
                    p.ResidentId = new string(chars);
                    break;
                case ErrorKind.BirthMismatch: // 며칠만 어긋나게 (04.02 ↔ 04.20 같은 실수 유도)
                    p.BirthDate = p.BirthDate.AddDays(_rng.Next(2) == 0 ? -(1 + _rng.Next(9)) : 1 + _rng.Next(9));
                    break;
                case ErrorKind.AddressMismatch:
                    string addr;
                    do addr = ResidentDatabase.MakeAddress(_rng); while (addr == p.Address);
                    p.Address = addr;
                    break;
                case ErrorKind.ForgedStamp:
                    p.Stamp = Rules.ForgedStamps[_rng.Next(Rules.ForgedStamps.Length)];
                    break;
                case ErrorKind.OutsiderAppearance: // 서류·사진은 진짜, 부스 얼굴만 미묘하게 다름
                    var f = v.Face;
                    if (_rng.Next(2) == 0) f.EyeOffset = _rng.Next(2) == 0 ? -1 : 1; else f.Neck = 1;
                    v.Face = f;
                    v.Line = OutsiderLines[_rng.Next(OutsiderLines.Length)];
                    break;
                case ErrorKind.QuarantineMissing:
                    v.Cert = null;
                    break;
                case ErrorKind.QuarantineExpired:
                    v.Cert ??= new QuarantineCert { Name = p.Name, Result = Rules.NegativeResult, Stamp = Rules.GenuineStamp };
                    v.Cert.TestDate = day.Today.AddDays(-(Rules.QuarantineValidDays + 1 + _rng.Next(6)));
                    break;
            }
        }
    }
}
