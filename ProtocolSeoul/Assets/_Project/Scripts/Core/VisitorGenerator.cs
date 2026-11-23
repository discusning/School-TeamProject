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
        // 3일차~ 말한 목적지 · 사유서 목적지 (목적지 이름은 가정)
        public static readonly string[] Destinations = { "강남 배급소", "용산 의료소", "마포 거주 구역", "종로 작업장" };

        // 강00 귓속말 (1주차 스토리 261122 강00 이벤트 ① 원문 그대로)
        public const string KangLine = "내가 벙커에 들어가게 해달라는 게 아니요. 곧 이 서류를 들고 당당하게 문을 두드릴 진00이라는 놈이 있을 거요. 그 놈은 짐승보다 못 한 놈이니 절대 들여보내지 마시오.";

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
            // 대본 인물 (에셋 ScriptedVisitors): 강00은 앞쪽, 진00은 그 뒤 끝 무렵에 옴 (순서 자리는 가정)
            if (day.ScriptedVisitors.Contains("kang")) list.Insert(Math.Min(1, list.Count), Kang(day));
            if (day.ScriptedVisitors.Contains("jin")) list.Insert(Math.Max(0, list.Count - 1), Jin(day));
            return list;
        }

        bool Speaks(DayConfig day) => day.Has(ErrorKind.SpokenNameMismatch) || day.Has(ErrorKind.AgeMismatch);
        static bool PermitDay(DayConfig day) => day.Has(ErrorKind.EntryMissing) || day.Has(ErrorKind.EntryNameMismatch) || day.Has(ErrorKind.EntryReasonDenied) || day.Has(ErrorKind.EntryDestMismatch);
        static bool CertDay(DayConfig day) => day.Has(ErrorKind.QuarantineMissing) || day.Has(ErrorKind.QuarantineExpired);

        public Visitor Generate(DayConfig day, ErrorKind kind, HashSet<string> used = null)
        {
            used ??= new HashSet<string>();
            var pool = (kind == ErrorKind.CriminalRecord ? _db.Criminals : _db.Clean).Where(r => !used.Contains(r.Name)).ToList();
            // 1일차 (서울 사람만): 서울 아님을 심을 때만 서울 밖 사람, 아니면 서울 사람
            if (day.Has(ErrorKind.NotSeoul))
            {
                var city = pool.Where(r => ResidentDatabase.IsSeoul(r.Address) == (kind != ErrorKind.NotSeoul)).ToList();
                if (city.Count > 0) pool = city;
            }
            if (pool.Count == 0) pool = _db.All.ToList();
            // 같은 날 같은 초상 그림이 두 번 나오지 않게 (남은 사람이 있을 때만)
            var fresh = pool.Where(x => !used.Contains("#P" + x.Face.Portrait)).ToList();
            if (fresh.Count > 0) pool = fresh;
            var r = pool[_rng.Next(pool.Count)];
            used.Add(r.Name);
            used.Add("#P" + r.Face.Portrait);

            var v = new Visitor
            {
                Face = r.Face,
                Pass = new TravelPass
                {
                    Name = r.Name, ResidentId = r.ResidentId, BirthDate = r.BirthDate, Address = r.Address,
                    Photo = r.Face, IssueDate = day.Today, Stamp = Rules.GenuineStamp, CriminalRecord = r.CriminalRecord,
                },
                Planted = kind,
            };
            if (CertDay(day))
                v.Cert = new QuarantineCert
                {
                    Name = r.Name, Result = Rules.NegativeResult, Stamp = Rules.GenuineStamp,
                    TestDate = day.Today.AddDays(-_rng.Next(0, Rules.QuarantineValidDays + 1)),
                };
            if (Speaks(day)) { v.SaidName = r.Name; v.SaidAge = Portraits.Age(r.BirthDate); }
            if (PermitDay(day))
            {
                v.SaidDest = Destinations[_rng.Next(Destinations.Length)];
                v.Permit = new EntryPermit { Name = r.Name, Reason = Rules.AllowedReasons[_rng.Next(Rules.AllowedReasons.Length)], Destination = v.SaidDest };
            }

            Plant(v, kind, day, used);
            v.Line ??= MakeLine(v);
            (v.Correct, v.Reason) = Rules.Judge(v, day, _db);
            return v;
        }

        // 첫마디: 1일차는 이름 · 나이를 말함, 3일차~는 목적지를 말함 (문장은 가정. 원문은 "나이를 물어보고", "방문자가 말한 목적지")
        string MakeLine(Visitor v)
        {
            var parts = new List<string>();
            if (v.SaidName != null) parts.Add($"{v.SaidName}입니다.");
            if (v.SaidAge >= 0) parts.Add($"나이는 {v.SaidAge}살이에요.");
            if (v.SaidDest != null) parts.Add($"{v.SaidDest}에 가야 합니다.");
            else parts.Add(Lines[_rng.Next(Lines.Length)]);
            return string.Join(" ", parts);
        }

        Resident Other(Visitor v, Func<Resident, bool> ok)
        {
            var c = _db.Clean.Where(x => x.Name != v.Pass.Name && ok(x)).ToList();
            return c.Count > 0 ? c[_rng.Next(c.Count)] : null;
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
                    if (v.Permit != null) v.Permit.Name = p.Name;
                    if (v.SaidName != null) v.SaidName = p.Name;
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
                case ErrorKind.PassMissing:
                    v.PassMissing = true;
                    break;
                case ErrorKind.PassDateWrong: // 발급일이 오늘이 아님: 1~3일 전 또는 1~2일 뒤 (범위는 가정)
                    p.IssueDate = _rng.Next(3) < 2 ? day.Today.AddDays(-(1 + _rng.Next(3))) : day.Today.AddDays(1 + _rng.Next(2));
                    break;
                case ErrorKind.AgeMismatch: // 말한 나이가 2~6살 다름
                    v.SaidAge = Math.Max(16, v.SaidAge + (_rng.Next(2) == 0 ? -1 : 1) * (2 + _rng.Next(5)));
                    if (v.SaidAge == Portraits.Age(p.BirthDate)) v.SaidAge += 3;
                    break;
                case ErrorKind.PhotoMismatch: // 다른 사람 얼굴 (초상이 다른 주민)
                    var o = Other(v, x => x.Face.Portrait != p.Photo.Portrait);
                    if (o != null) v.Face = o.Face;
                    break;
                case ErrorKind.SpokenNameMismatch:
                    var n = Other(v, x => true);
                    v.SaidName = n != null ? n.Name : "무명";
                    break;
                case ErrorKind.NotSeoul: // 서울 밖 사람을 이미 뽑음 (Generate)
                    break;
                case ErrorKind.EntryMissing:
                    v.Permit = null;
                    break;
                case ErrorKind.EntryNameMismatch:
                    if (v.Permit != null) { var m = Other(v, x => true); v.Permit.Name = m != null ? m.Name : "무명"; }
                    break;
                case ErrorKind.EntryReasonDenied:
                    if (v.Permit != null) v.Permit.Reason = Rules.DeniedReasons[_rng.Next(Rules.DeniedReasons.Length)];
                    break;
                case ErrorKind.EntryDestMismatch:
                    if (v.Permit != null)
                    {
                        var ds = Destinations.Where(d => d != v.SaidDest).ToArray();
                        v.Permit.Destination = ds[_rng.Next(ds.Length)];
                    }
                    break;
            }
        }

        // 강00 (56세, 남, 외부인): 정식 서류 없이 창살 너머 귓속말 (3일차)
        Visitor Kang(DayConfig day)
        {
            var birth = new DateTime(1993, 11, 2);
            const string id = "931102-1530271";
            var face = new Face(2, 2) { Portrait = Portraits.Pick(birth, id, 0) };
            var v = new Visitor
            {
                Story = "kang", Line = KangLine, Face = face, PassMissing = true,
                Pass = new TravelPass { Name = ResidentDatabase.KangName, ResidentId = id, BirthDate = birth, Address = "", Photo = face, IssueDate = day.Today, Stamp = "" },
            };
            (v.Correct, v.Reason) = Rules.Judge(v, day, _db);
            return v;
        }

        // 진00 (20대 후반, 남): 위조 서류지만 겉으로는 완벽 (5일차 전에는 판별할 규칙 없음). 첫마디는 가정
        Visitor Jin(DayConfig day)
        {
            var r = _db.FindByName(ResidentDatabase.JinName);
            string dest = Destinations[2];
            var v = new Visitor
            {
                Story = "jin", Face = r.Face, Planted = ErrorKind.None,
                Pass = new TravelPass { Name = r.Name, ResidentId = r.ResidentId, BirthDate = r.BirthDate, Address = r.Address, Photo = r.Face, IssueDate = day.Today, Stamp = Rules.GenuineStamp },
                Line = $"안녕하십니까. 서류는 모두 준비했습니다. {dest}에 가야 합니다.",
            };
            if (CertDay(day)) v.Cert = new QuarantineCert { Name = r.Name, Result = Rules.NegativeResult, Stamp = Rules.GenuineStamp, TestDate = day.Today };
            if (PermitDay(day)) { v.SaidDest = dest; v.Permit = new EntryPermit { Name = r.Name, Reason = "배정 근무", Destination = dest }; }
            (v.Correct, v.Reason) = Rules.Judge(v, day, _db);
            return v;
        }
    }
}
