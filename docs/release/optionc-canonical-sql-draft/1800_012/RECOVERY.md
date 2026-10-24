# 1800_012 candidate recovery

Before `COMMIT`, any error rolls back the constraint replacement and version
row. After a successful commit, preserve all request/event audit and use only
a reviewed forward-fix or IT-controlled restore decision. Never downgrade a
live schema by dropping `Applied` audit states. A new UAT migration does not
enable B3, Approve/Apply, deployment or formal Business UAT.
