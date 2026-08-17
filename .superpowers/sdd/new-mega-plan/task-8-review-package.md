# Review package: f64b9af..10735e4

## Commits
10735e4 fix(audio): P1.3 last-track completeness rule ΓÇö <=0 fails, short warns

## Files changed
 src/Services/Audio/FlacCompletenessChecker.cs | 12 ++++++++++--
 1 file changed, 10 insertions(+), 2 deletions(-)

## Diff
diff --git a/src/Services/Audio/FlacCompletenessChecker.cs b/src/Services/Audio/FlacCompletenessChecker.cs
index 832e356..f648077 100644
--- a/src/Services/Audio/FlacCompletenessChecker.cs
+++ b/src/Services/Audio/FlacCompletenessChecker.cs
@@ -65,34 +65,42 @@ public sealed class FlacCompletenessChecker(SoxService sox)
 				return new DurationCheckResult(
 					false,
 					trackNumberCount,
 					primaryFlacCount,
 					dffDir
 				);
 			}
 		}
 		else if (track == cueTracks[^1])
 		{
-			if (durationResult.Value.TotalSeconds < 30.0)
+			if (durationResult.Value.TotalSeconds <= 0)
 			{
-				Telemetry.Info(
+				Telemetry.Warn(
 					"Pipeline.LastTrackTooShort dir={Dir} duration={Duration:F1}s",
 					LogPaths.Format(dffDir),
 					durationResult.Value.TotalSeconds
 				);
 				return new DurationCheckResult(
 					false,
 					trackNumberCount,
 					primaryFlacCount,
 					dffDir
 				);
 			}
+			if (durationResult.Value.TotalSeconds < 30.0)
+			{
+				Telemetry.Warn(
+					"Pipeline.LastTrackShort dir={Dir} duration={Duration:F1}s",
+					LogPaths.Format(dffDir),
+					durationResult.Value.TotalSeconds
+				);
+			}
 		}
 	}
 
 	return new DurationCheckResult(
 		true,
 		trackNumberCount,
 		primaryFlacCount,
 		dffDir
 	);
 	}
