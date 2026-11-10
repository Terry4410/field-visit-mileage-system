#!/usr/bin/env bash
# NONDEPLOY correction of the recovered outer launcher.  This file retains
# every original gate and changes only SQL target acquisition/validation from
# lossy TSV/read parsing to status-checked structured JSON.

set +x
Z="$HOME/FieldVisit_PathB_G1_Consolidated_SQLCMD_ArgFix_SafetyFix_NONDEPLOY_20261110.zip"
D="$HOME/fv-g1-1050afb0"
A="$HOME/fv-g1-1050afb0-authorization.json"
fail(){ echo "G1_PREGATE=HOLD_$1"; return 1 2>/dev/null || exit 1; }
[ -f "$Z" ] || fail ZIP_MISSING
[ "$(sha256sum "$Z" | cut -d' ' -f1)" = "8cc7a5aa4d888d1958380137ea6af46bf02d329b89850e7b27dcb26c0ac08141" ] || fail ZIP_HASH
[ ! -e "$D" ] || fail LOCAL_COLLISION
mkdir -m 700 "$D" || fail LOCAL_STAGE
unzip -q "$Z" -d "$D" || fail ZIP_CRC
[ "$(sha256sum "$D/binding-index.json" | cut -d' ' -f1)" = "302216e00544d5dfcf0821fa362e2b0f6aebb7e0125121509f8bdfc8fabb6c45" ] || fail BINDING_HASH
( cd "$D" && sha256sum -c SHA256SUMS >/dev/null ) || fail FILE_BINDING
echo G1_ZIP_BINDING=PASS
az account show --query '{id:id,tenantId:tenantId,userType:user.type}' -o json --only-show-errors | python3 -c 'import sys,json,hashlib; x=json.load(sys.stdin); h=lambda v:hashlib.sha256(str(v).lower().encode()).hexdigest(); raise SystemExit(0 if h(x.get("id"))=="7dca8e3016693051656ddfa5efa77b92c2f0ea679ab140eb237a818c130d1b47" and h(x.get("tenantId"))=="976f4269f73c61a6c7b15538a6518272f937428e59fdd8f87d606a8501d74c9c" and str(x.get("userType")).lower()=="user" else 1)' || fail OWNER_ACCOUNT
az ad signed-in-user show --query id -o tsv --only-show-errors | python3 -c 'import sys,hashlib; v=sys.stdin.read().strip().lower(); raise SystemExit(0 if hashlib.sha256(v.encode()).hexdigest()=="2e3687eea0c142b70b4923094ea37c2e8a17f97815db894b58ae60379fccd604" else 1)' || fail OWNER_OID
az version -o json --only-show-errors | python3 -c 'import sys,json; from packaging.version import Version; x=json.load(sys.stdin); raise SystemExit(0 if Version(x["azure-cli"])>=Version("2.90.0") else 1)' || fail AZ_CLI
INST=$(az webapp list-instances -g rg-fieldvisit-uat -n api-fieldvisit-pathb-uat -o json --only-show-errors | python3 -c 'import sys,json; x=json.load(sys.stdin); ok=isinstance(x,list) and len(x)==1 and isinstance(x[0],dict) and x[0].get("name") and x[0].get("name")==x[0].get("instanceId"); print(x[0]["name"] if ok else ""); raise SystemExit(0 if ok else 1)') || fail INSTANCE
[ -n "$INST" ] || fail INSTANCE
INST_SHA=$(printf '%s' "$INST" | tr '[:upper:]' '[:lower:]' | sha256sum | cut -d' ' -f1)
SQL_TARGET_JSON=$(az sql server show -g rg-fieldvisit-uat -n sql-fieldvisit-jpe-pathb-uat --query '{fqdn:fullyQualifiedDomainName,pna:publicNetworkAccess,tls:minimalTlsVersion}' -o json --only-show-errors) || fail SQL_TARGET
printf '%s' "$SQL_TARGET_JSON" | python3 -c 'import json,sys; x=json.load(sys.stdin); ok=isinstance(x,dict) and set(x)=={"fqdn","pna","tls"} and x.get("fqdn")=="sql-fieldvisit-jpe-pathb-uat.database.windows.net" and x.get("pna")=="Disabled" and x.get("tls")=="1.2"; raise SystemExit(0 if ok else 1)' || fail SQL_TARGET
unset SQL_TARGET_JSON
[ "$(az sql server firewall-rule list -g rg-fieldvisit-uat -s sql-fieldvisit-jpe-pathb-uat --query 'length(@)' -o tsv --only-show-errors)" = "0" ] || fail FIREWALL
az sql server ad-admin list -g rg-fieldvisit-uat -s sql-fieldvisit-jpe-pathb-uat --query '[].sid' -o tsv --only-show-errors | python3 -c 'import sys,hashlib; want="2e3687eea0c142b70b4923094ea37c2e8a17f97815db894b58ae60379fccd604"; vals=[x.strip().lower() for x in sys.stdin if x.strip()]; raise SystemExit(0 if any(hashlib.sha256(v.encode()).hexdigest()==want for v in vals) else 1)' || fail SQL_ADMIN
read APPSTATE APPNAME VNET <<EOF
$(az webapp show -g rg-fieldvisit-uat -n api-fieldvisit-pathb-uat --query '[state,name,virtualNetworkSubnetId]' -o tsv --only-show-errors)
EOF
[ "$APPSTATE" = "Running" ] && [ "$APPNAME" = "api-fieldvisit-pathb-uat" ] && [ -n "$VNET" ] || fail APP_TARGET
export INST_SHA
python3 - "$A" <<'PY'
import json,os,secrets,sys
from datetime import datetime,timedelta,timezone
p=sys.argv[1]
now=datetime.now(timezone.utc)
obj={
 "operation_id":secrets.token_hex(16),
 "contract":"FIELDVISIT_PATHB_G1_INSTALL_V1",
 "decision":"OWNER_APPROVED_PATHB_G1_ORIGINAL_INSTALLER_SINGLE",
 "artifact_sha256":"8cc7a5aa4d888d1958380137ea6af46bf02d329b89850e7b27dcb26c0ac08141",
 "binding_index_sha256":"302216e00544d5dfcf0821fa362e2b0f6aebb7e0125121509f8bdfc8fabb6c45",
 "server":"sql-fieldvisit-jpe-pathb-uat.database.windows.net",
 "database":"db-fieldvisit-pathb-uat",
 "installation_attempts":1,
 "instance_sha256":os.environ["INST_SHA"],
 "not_before":(now-timedelta(seconds=10)).isoformat(timespec="seconds").replace("+00:00","Z"),
 "expires_at":(now+timedelta(minutes=29)).isoformat(timespec="seconds").replace("+00:00","Z"),
}
fd=os.open(p,os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600)
with os.fdopen(fd,"w",encoding="utf-8") as f:
 json.dump(obj,f,separators=(",",":")); f.write("\n")
PY
[ -f "$A" ] || fail AUTHORIZATION
echo G1_PREGATE=PASS
echo G1_AUTHORIZATION=READY

