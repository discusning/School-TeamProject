using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ProtocolSeoul.Tests
{
    // P0 확인: 화면 없이 방문자 생성·정답 계산·검사 모드 비교가 맞는지.
    // 실행: Unity Test Runner (EditMode) 또는 -runTests -testPlatform EditMode
    public class CoreTests
    {
        static ResidentDatabase Db() => ResidentDatabase.Generate(seed: 2050);
        static DayConfig Day(int n) => DayConfig.Defaults()[n - 1];

        [Test]
        public void Database_HasUniqueNamesAndValidIds()
        {
            var db = Db();
            Assert.AreEqual(61, db.All.Count); // 주민 60 + 대본 인물 진00
            foreach (var r in db.All)
            {
                StringAssert.StartsWith(r.BirthDate.ToString("yyMMdd"), r.ResidentId);
                Assert.AreEqual(14, r.ResidentId.Length);
            }
            Assert.Greater(db.Criminals.Count(), 0);
        }

        [Test]
        public void CleanVisitor_IsPass_OnEveryDay()
        {
            var db = Db();
            for (int d = 1; d <= 3; d++)
            {
                var gen = new VisitorGenerator(db, seed: d);
                for (int i = 0; i < 30; i++)
                {
                    var v = gen.Generate(Day(d), ErrorKind.None);
                    Assert.AreEqual(Verdict.Pass, v.Correct, $"{d}일차 문제 없는 방문자가 거부됨: {v.Reason}");
                }
            }
        }

        // 심은 오류는 반드시 그 오류 이유로 거부(얼굴 이상은 사살)되어야 한다 (생성기와 판정이 같은 기준인지)
        [Test]
        public void EveryPlantedError_IsDeniedForThatReason()
        {
            var db = Db();
            var gen = new VisitorGenerator(db, seed: 7);
            for (int d = 1; d <= 3; d++)
            {
                var day = Day(d);
                foreach (var kind in day.Rules)
                    for (int i = 0; i < 20; i++)
                    {
                        var v = gen.Generate(day, kind);
                        var want = kind == ErrorKind.OutsiderAppearance ? Verdict.Kill : Verdict.Deny;
                        Assert.AreEqual(want, v.Correct, $"{d}일차 {kind} 방문자 판정이 {v.Correct}");
                        Assert.AreEqual(kind, v.Reason, $"{d}일차 {kind}를 심었는데 이유가 {v.Reason}");
                    }
            }
        }

        // 1일차 DB 없음: DB에 없는 이름이어도 DB 규칙으로 거부하지 않음. 3일차 대본 인물: 강00 통행증 없음 → 거부, 진00 서류 완벽 → 통과
        [Test]
        public void Day1_NoDb_Day3_StoryVisitors()
        {
            var db = Db();
            Assert.IsFalse(Day(1).Has(ErrorKind.NotInDatabase));
            var list = new VisitorGenerator(db, 11).GenerateDay(Day(3));
            var kang = list.Single(v => v.Story == "kang");
            var jin = list.Single(v => v.Story == "jin");
            Assert.AreEqual(Verdict.Deny, kang.Correct);
            Assert.AreEqual(ErrorKind.PassMissing, kang.Reason);
            Assert.AreEqual(Verdict.Pass, jin.Correct, $"진00 이유 {jin.Reason}");
            Assert.Less(list.IndexOf(kang), list.IndexOf(jin));
        }

        [Test]
        public void GeneratedDays_MatchConfig_AndHaveAtLeastOneError()
        {
            var db = Db();
            foreach (var day in DayConfig.Defaults())
                for (int seed = 0; seed < 20; seed++)
                {
                    var list = new VisitorGenerator(db, seed).GenerateDay(day);
                    Assert.AreEqual(day.VisitorCount + day.ScriptedVisitors.Count, list.Count);
                    Assert.IsTrue(list.Any(v => v.Correct != Verdict.Pass), $"{day.Day}일차 seed {seed}: 오류 방문자 없음");
                    Assert.IsTrue(list.All(v => v.Planted == ErrorKind.None || day.Has(v.Planted)), "꺼진 규칙의 오류가 심어짐");
                    Assert.AreEqual(list.Count, list.Select(v => v.Pass.Name).Distinct().Count(), "같은 사람이 하루에 두 번 옴");
                }
        }

        [Test]
        public void Day1_HasNoQuarantineCert_Day3_HasIt()
        {
            var db = Db();
            var gen = new VisitorGenerator(db, 1);
            Assert.IsNull(gen.Generate(Day(1), ErrorKind.None).Cert);
            Assert.IsNotNull(gen.Generate(Day(3), ErrorKind.None).Cert);
            Assert.IsNull(gen.Generate(Day(3), ErrorKind.QuarantineMissing).Cert);
        }

        [Test]
        public void Inspector_ComparesOnlySameField()
        {
            var db = Db();
            var gen = new VisitorGenerator(db, 3);
            var v = gen.Generate(Day(1), ErrorKind.BirthMismatch);
            var rec = db.FindByName(v.Pass.Name);
            Assert.AreEqual(CompareResult.Mismatch, Inspector.Compare(v, rec, Source.TravelPass, Field.BirthDate, Source.Database, Field.BirthDate));
            Assert.AreEqual(CompareResult.Match, Inspector.Compare(v, rec, Source.TravelPass, Field.Address, Source.Database, Field.Address));
            Assert.AreEqual(CompareResult.Unrelated, Inspector.Compare(v, rec, Source.TravelPass, Field.BirthDate, Source.Database, Field.Address));
            Assert.AreEqual(CompareResult.Unrelated, Inspector.Compare(v, rec, Source.TravelPass, Field.Name, Source.TravelPass, Field.Name));
        }

        [Test]
        public void Inspector_FindsOutsiderFace()
        {
            var db = Db();
            var v = new VisitorGenerator(db, 4).Generate(Day(2), ErrorKind.OutsiderAppearance);
            var rec = db.FindByName(v.Pass.Name);
            Assert.AreEqual(CompareResult.Mismatch, Inspector.Compare(v, rec, Source.Booth, Field.Photo, Source.Database, Field.Photo));
            Assert.AreEqual(CompareResult.Match, Inspector.Compare(v, rec, Source.TravelPass, Field.Photo, Source.Database, Field.Photo));
        }

        // 초상 (C038): 모든 주민이 초상을 받고, 초상 캡션의 성별이 주민번호 성별과 같음. 서류 사진 = DB 사진.
        [Test]
        public void EveryResident_HasPortrait_MatchingSexAndAge()
        {
            var db = Db();
            foreach (var r in db.All)
            {
                Assert.That(r.Face.Portrait, Is.InRange(1, 30), r.Name);
                var e = Portraits.Get(r.Face.Portrait);
                bool male = Portraits.IsMale(r.ResidentId);
                Assert.IsTrue(e.Male == null || e.Male == male, $"{r.Name} 성별 다름: {e.Caption}");
                CollectionAssert.Contains(Portraits.Candidates(Portraits.Age(r.BirthDate), male).Select(c => c.Id).ToList(), e.Id);
            }
            var v = new VisitorGenerator(db, 9).Generate(Day(1), ErrorKind.None);
            Assert.AreEqual(v.Face, v.Pass.Photo);
            Assert.AreEqual(db.FindByName(v.Pass.Name).Face, v.Pass.Photo);
        }

        [Test]
        public void Quarantine_ExpiresAfterThreeDays()
        {
            var today = new DateTime(2050, 3, 3);
            Assert.IsFalse(Rules.CertExpired(new QuarantineCert { TestDate = today.AddDays(-3) }, today));
            Assert.IsTrue(Rules.CertExpired(new QuarantineCert { TestDate = today.AddDays(-4) }, today));
        }

        [Test]
        public void Ledger_TwoFreeWarnings_ThenPenalty()
        {
            var db = Db();
            var gen = new VisitorGenerator(db, 5);
            var ledger = new DayLedger();
            var clean = new List<Visitor>();
            for (int i = 0; i < 5; i++) clean.Add(gen.Generate(Day(1), ErrorKind.None));
            ledger.Record(clean[0], Verdict.Pass);   // 맞음
            ledger.Record(clean[1], Verdict.Pass);   // 맞음
            ledger.Record(clean[2], Verdict.Deny);   // 실수 1
            ledger.Record(clean[3], Verdict.Deny);   // 실수 2
            ledger.Record(clean[4], Verdict.Deny);   // 실수 3 → 차감
            Assert.AreEqual(2, ledger.CorrectCount);
            Assert.AreEqual(3, ledger.Mistakes);
            Assert.AreEqual(2 * DayLedger.RationPerCorrect - DayLedger.PenaltyPerMistake, ledger.Total);
            Assert.AreEqual("식량 감소", ledger.Stage);
            ledger.Record(gen.Generate(Day(1), ErrorKind.None), Verdict.Deny); // 실수 4 → 식량 미지급
            Assert.AreEqual(0, ledger.Total);
            Assert.IsFalse(ledger.Fired);
            ledger.Record(gen.Generate(Day(1), ErrorKind.None), Verdict.Deny); // 실수 5 → 퇴출
            Assert.IsTrue(ledger.Fired);
        }
    }
}
