# P4.3 - Runtime observation of static-only criteria

1. T8 real conversion log equality: GainCalcComplete and master Saracon.ConvertStart same rate/depth.
2. T3 real `--format 16` SACD conversion end-to-end.
3. T7 real full Saracon conversion, estimator vs output size.
4. T9 runtime ownership: CUE retained through forced probe failure; cleanup exception does not mask primary error.
5. Fix/account for mangled temp-root label in Saracon.ConvertStart.
6. Confirm Seq sink level deferral intended/corrected.
7. Confirm Phase 5 gates do not depend on unreadable log fields.

Acceptance: four static-only criteria observed in real logs with entries quoted; rendering defects fixed/accounted. HALT on RegistryOleInit signature.

Reporting: command/raw output/PASS/FAIL/BLOCKED per subtask, exact owner/signature. Write task-23-report.md. No inferred PASS.
