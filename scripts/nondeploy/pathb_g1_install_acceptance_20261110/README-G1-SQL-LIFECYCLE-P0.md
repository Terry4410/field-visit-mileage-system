# G1 P0 — all 19 SQL calls process safety (NONDEPLOY)

This is an integrated source-level correction, NOT live authorization or proof of Azure compatibility.

The existing process lifecycle guard covered only the 16-call Installer. This correction extends direct SQL client process-group ownership to the integrated preflight and two postchecks. The G1 outer runner must fail closed and preserve ephemeral credentials if a SQL child process group cannot be cleared; it must NEVER interpret a kill as a database rollback.

Changes: `p3_app_installer_runner.py` SQL call lifecycle only. Original 14 SQL layers, frozen installer and original ODBC adapter are untouched. 3 new focused local tests passed (success, timeout with child, and child surviving nominal parent exit). Previously accepted tests are not represented as rerun.

Known unverified: actual Azure SSH/Blob protocol, disconnection behavior, detached process not sharing the controlled group, real SQL result/partial DDL and effective Azure identity permissions. Thus LIVE_GO=NO; original consumed attempts forbidden and no Cloud Shell commands are supplied.
