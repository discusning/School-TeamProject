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
            Assert.AreEqual(62, db.All.Count); // 주민 60 + 대본 인물 진00 · 강00 (C061)
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
                    Assert.AreEqual(day.VisitorCount + day.ScriptedVisitors.Count(s => s != VisitorGenerator.KangForged), list.Count); // 4일차 강00은 진00 통과 때만 (C061)
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
            Assert.IsNotNull(gen.Generate(Day(2), ErrorKind.None).Cert); // C060: 2일차부터 (3차 회의록)
            Assert.IsNotNull(gen.Generate(Day(3), ErrorKind.None).Cert);
            Assert.IsNull(gen.Generate(Day(3), ErrorKind.QuarantineMissing).Cert);
        }

        // C060: 임시통행증 생년월일 칸은 모든 일차. 범죄기록 칸은 2일차~ (DB 복구 뒤)
        [Test]
        public void PassFields_HaveBirthDate_EveryDay()
        {
            foreach (var day in DayConfig.Defaults())
            {
                CollectionAssert.Contains(day.PassFields, Field.BirthDate, $"{day.Day}일차 통행증에 생년월일 칸 없음");
                CollectionAssert.Contains(day.PassFields, Field.IssueDate, $"{day.Day}일차 통행증에 발급일 칸 없음");
                Assert.AreEqual(day.Day >= 2, day.PassFields.Contains(Field.CriminalRecord), $"{day.Day}일차 범죄기록 칸");
            }
        }

        // C060: 정상 방문자의 발급일 = 화면 왼쪽 위 날짜. 발급일을 바꾸는 오류는 PassDateWrong 하나뿐
        [Test]
        public void IssueDate_EqualsDayLabel_UnlessPassDateWrong()
        {
            var db = Db();
            var gen = new VisitorGenerator(db, 21);
            foreach (var day in DayConfig.Defaults())
            {
                StringAssert.EndsWith("-" + Fmt.Date(day.Today), day.DayLabel);
                Assert.AreEqual($"{day.Day}일차-2050.03.{day.Day:00}", day.DayLabel);
                foreach (var kind in day.Rules.Prepend(ErrorKind.None))
                    for (int i = 0; i < 10; i++)
                    {
                        var v = gen.Generate(day, kind);
                        string issue = Fmt.Date(v.Pass.IssueDate);
                        if (kind == ErrorKind.PassDateWrong) Assert.IsFalse(day.DayLabel.EndsWith("-" + issue), $"{day.Day}일차 날짜 오류인데 발급일 {issue}");
                        else Assert.IsTrue(day.DayLabel.EndsWith("-" + issue), $"{day.Day}일차 {kind} 발급일 {issue} ≠ {day.DayLabel}");
                        if (!v.PassMissing) Assert.AreEqual(issue, Inspector.Value(v, null, Source.TravelPass, Field.IssueDate));
                    }
                foreach (var v in new VisitorGenerator(db, 5).GenerateDay(day).Where(x => x.Story != null))
                    Assert.AreEqual(day.Today, v.Pass.IssueDate, $"{v.Story} 발급일");
            }
        }

        // C060: 지역 설정이 달라도(태국 불기 달력·독일 등) 날짜 글자가 같음
        [Test]
        public void DateText_IgnoresCulture()
        {
            var old = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                foreach (var c in new[] { "th-TH", "de-DE", "ko-KR", "ar-SA" })
                {
                    System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(c);
                    Assert.AreEqual("2일차-2050.03.02", Day(2).DayLabel, c);
                    Assert.AreEqual(new DateTime(2050, 3, 2), Day(2).Today, c);
                }
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = old; }
        }

        // C060: 일차별 자료 (C058 항목 3 표, 검역 소견서는 3차 회의록 "2일차부터")
        // 1일차 통행증만 · DB 없음 / 2일차 통행증 · DB · 검역 소견서 / 3일차 + 입성 사유서. 위조 판별은 5일차라 1~3일차 없음
        [Test]
        public void DocsPerDay_MatchC058Table()
        {
            var db = Db();
            var want = new[] { (db: false, cert: false, permit: false), (db: true, cert: true, permit: false), (db: true, cert: true, permit: true), (db: true, cert: true, permit: true) };
            foreach (var day in DayConfig.Defaults())
            {
                var w = want[day.Day - 1];
                Assert.AreEqual(w.db, day.HasDatabase, $"{day.Day}일차 DB");
                Assert.AreEqual(w.cert, day.HasCert, $"{day.Day}일차 검역 소견서");
                Assert.AreEqual(w.permit, day.HasPermit, $"{day.Day}일차 입성 사유서");
                Assert.IsFalse(day.HasForgery, $"{day.Day}일차 위조 판별");
                foreach (var v in new VisitorGenerator(db, 8).GenerateDay(day).Where(x => x.Story == null))
                {
                    if (!w.cert) Assert.IsNull(v.Cert, $"{day.Day}일차에 검역 소견서가 나옴");
                    if (!w.permit) Assert.IsNull(v.Permit, $"{day.Day}일차에 입성 사유서가 나옴");
                }
                var clean = new VisitorGenerator(db, 9).Generate(day, ErrorKind.None);
                Assert.AreEqual(w.cert, clean.Cert != null, $"{day.Day}일차 문제 없는 방문자 검역 소견서");
                Assert.AreEqual(w.permit, clean.Permit != null, $"{day.Day}일차 문제 없는 방문자 입성 사유서");
            }
        }

        // C060: 게임이 읽는 에셋(Resources/Days)이 기본값과 같은 규칙·날짜 (테스트가 에셋과 다른 규칙을 보지 않게)
        [Test]
        public void DayAssets_MatchDefaults()
        {
            var assets = UnityEngine.Resources.LoadAll<DayConfig>("Days").OrderBy(d => d.Day).ToList();
            var defs = DayConfig.Defaults();
            Assert.AreEqual(defs.Count, assets.Count);
            for (int i = 0; i < defs.Count; i++)
            {
                Assert.AreEqual(defs[i].Day, assets[i].Day);
                Assert.AreEqual(defs[i].Date, assets[i].Date, $"{defs[i].Day}일차 날짜");
                CollectionAssert.AreEqual(defs[i].Rules, assets[i].Rules, $"{defs[i].Day}일차 규칙");
                CollectionAssert.AreEqual(defs[i].PassFields, assets[i].PassFields);
            }
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

        // C061: 검사 모드에서 대화 로그 말풍선 ↔ 서류 칸. 말과 서류가 다르면 불일치, 그 방문자는 그 이유로 거부
        [Test]
        public void Inspector_ComparesLogWithDocs()
        {
            var db = Db();
            var gen = new VisitorGenerator(db, 61);
            var checks = new[]
            {
                (day: 1, kind: ErrorKind.SpokenNameMismatch, src: Source.TravelPass, f: Field.Name),
                (day: 1, kind: ErrorKind.AgeMismatch, src: Source.TravelPass, f: Field.BirthDate),
                (day: 3, kind: ErrorKind.EntryDestMismatch, src: Source.EntryPermit, f: Field.Destination),
            };
            foreach (var c in checks)
                for (int i = 0; i < 20; i++)
                {
                    var ok = gen.Generate(Day(c.day), ErrorKind.None);
                    Assert.AreEqual(CompareResult.Match, Inspector.Compare(ok, db.FindByName(ok.Pass.Name), Source.Log, c.f, c.src, c.f), $"{c.day}일차 {c.f} 정상");
                    Assert.AreEqual(CompareResult.Match, Inspector.Compare(ok, db.FindByName(ok.Pass.Name), c.src, c.f, Source.Log, Field.Name), $"{c.day}일차 {c.f} 순서 반대 (말풍선 칸 종류는 무시)");
                    var bad = gen.Generate(Day(c.day), c.kind);
                    Assert.AreEqual(CompareResult.Mismatch, Inspector.Compare(bad, db.FindByName(bad.Pass.Name), Source.Log, Field.Name, c.src, c.f), $"{c.kind}");
                    Assert.AreEqual(c.kind, bad.Reason);
                    Assert.AreEqual(Verdict.Deny, bad.Correct);
                }
            // 3일차: 말한 목적지가 없는 칸(사유 · 주소)·같은 말풍선끼리는 관련 없음. 1일차 말풍선 ↔ 사유서 없음 → 관련 없음
            var v3 = gen.Generate(Day(3), ErrorKind.None);
            var r3 = db.FindByName(v3.Pass.Name);
            Assert.AreEqual(CompareResult.Unrelated, Inspector.Compare(v3, r3, Source.Log, Field.Name, Source.EntryPermit, Field.Reason));
            Assert.AreEqual(CompareResult.Unrelated, Inspector.Compare(v3, r3, Source.Log, Field.Name, Source.TravelPass, Field.Address));
            Assert.AreEqual(CompareResult.Unrelated, Inspector.Compare(v3, r3, Source.Log, Field.Name, Source.Log, Field.Name));
            var v1 = gen.Generate(Day(1), ErrorKind.None);
            Assert.AreEqual(CompareResult.Unrelated, Inspector.Compare(v1, null, Source.Log, Field.Name, Source.EntryPermit, Field.Destination));
            // 통행증을 안 낸 방문자: 통행증 칸 없음 → 관련 없음
            var miss = gen.Generate(Day(1), ErrorKind.PassMissing);
            Assert.AreEqual(CompareResult.Unrelated, Inspector.Compare(miss, null, Source.Log, Field.Name, Source.TravelPass, Field.Name));
        }

        // C061: 4일차 = 3일차와 같은 확인 항목. 강00은 3일차 진00을 통과시켰을 때만 위조 서류로 옴 (분기 B·C), 진00 거부면 안 옴 (분기 A)
        [Test]
        public void Day4_KangComesOnlyIfJinPassed()
        {
            var db = Db();
            CollectionAssert.AreEqual(Day(3).Rules, Day(4).Rules);
            Assert.AreEqual("2050-03-04", Day(4).Date);
            var day3Kang = new VisitorGenerator(db, 2).GenerateDay(Day(3)).Single(v => v.Story == "kang");
            for (int seed = 0; seed < 20; seed++)
            {
                Assert.IsFalse(new VisitorGenerator(db, seed).GenerateDay(Day(4)).Any(v => v.Story == VisitorGenerator.KangForged), "진00 거부인데 강00이 옴");
                var list = new VisitorGenerator(db, seed) { JinPassed = true }.GenerateDay(Day(4));
                var kang = list.Single(v => v.Story == VisitorGenerator.KangForged);
                Assert.AreEqual(Day(4).VisitorCount + 1, list.Count);
                Assert.AreEqual(ResidentDatabase.KangName, kang.Pass.Name);
                Assert.AreEqual(day3Kang.Face, kang.Face, "3일차와 같은 얼굴");
                Assert.AreEqual(Verdict.Pass, kang.Correct, $"위조 서류가 겉으로 완벽해야 함: {kang.Reason}");
                Assert.AreEqual(list.Count, list.Select(v => v.Pass.Name).Distinct().Count());
            }
            // 일반 방문자로 강00 · 진00이 뽑히지 않음
            var gen = new VisitorGenerator(db, 3);
            for (int i = 0; i < 200; i++)
            {
                var v = gen.Generate(Day(4), (ErrorKind)(i % 2 == 0 ? 0 : 7));
                Assert.AreNotEqual(ResidentDatabase.KangName, v.Pass.Name);
                Assert.AreNotEqual(ResidentDatabase.JinName, v.Pass.Name);
            }
        }

        // C061: 자폭 배드 엔딩 = 3일차 진00 통과 + 4일차 강00 통과 (분기 B). 강00 처분은 실수로 세지 않음
        [Test]
        public void BombEnding_OnlyWhenJinThenKangPassed()
        {
            Assert.IsTrue(DayLedger.BombEnding(true, Verdict.Pass));
            Assert.IsFalse(DayLedger.BombEnding(true, Verdict.Deny));
            Assert.IsFalse(DayLedger.BombEnding(true, Verdict.Kill));
            Assert.IsFalse(DayLedger.BombEnding(false, Verdict.Pass));
            var db = Db();
            var kang = new VisitorGenerator(db, 1) { JinPassed = true }.GenerateDay(Day(4)).Single(v => v.Story == VisitorGenerator.KangForged);
            var ledger = new DayLedger();
            ledger.Record(kang, Verdict.Deny);
            Assert.AreEqual(0, ledger.Mistakes);
            Assert.AreEqual(0, ledger.CorrectCount);
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
