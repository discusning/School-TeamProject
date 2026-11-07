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
            Assert.AreEqual(60, db.All.Count);
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

        // 심은 오류는 반드시 그 오류 이유로 거부되어야 한다 (생성기와 판정이 같은 기준인지)
        [Test]
        public void EveryPlantedError_IsDeniedForThatReason()
        {
            var db = Db();
            var day3 = Day(3); // 모든 규칙이 켜진 날
            var gen = new VisitorGenerator(db, seed: 7);
            foreach (var kind in day3.Rules)
                for (int i = 0; i < 20; i++)
                {
                    var v = gen.Generate(day3, kind);
                    Assert.AreEqual(Verdict.Deny, v.Correct, $"{kind} 방문자가 통과됨");
                    Assert.AreEqual(kind, v.Reason, $"{kind}를 심었는데 이유가 {v.Reason}");
                }
        }

        [Test]
        public void GeneratedDays_MatchConfig_AndHaveAtLeastOneError()
        {
            var db = Db();
            foreach (var day in DayConfig.Defaults())
                for (int seed = 0; seed < 20; seed++)
                {
                    var list = new VisitorGenerator(db, seed).GenerateDay(day);
                    Assert.AreEqual(day.VisitorCount, list.Count);
                    Assert.IsTrue(list.Any(v => v.Correct == Verdict.Deny), $"{day.Day}일차 seed {seed}: 오류 방문자 없음");
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
        }
    }
}
