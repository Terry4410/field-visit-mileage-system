"""Offline synthetic management snapshots: no Azure/Token/SQL/Blob."""
from copy import deepcopy
import unittest
from unittest.mock import patch
from p3_ssh_transport_contract import OWNER_SHA, TENANT_SHA, SUB_SHA
import pathb_select22_management_snapshot_nondeploy as gate


class SnapshotTests(unittest.TestCase):
    def setUp(self):
        self.account = {"id": "synthetic-sub", "tenantId": "synthetic-tenant",
                        "user": {"type": "user"}}
        self.owner = {"id": "synthetic-owner"}
        self.app = {"name": gate.APP, "resourceGroup": gate.RG,
                    "state": "Running", "virtualNetworkSubnetId": "private-subnet"}
        self.instances = [{"name": "instance01234567"}]
        self.sql = {"name": gate.SQL_RESOURCE,
                    "fullyQualifiedDomainName": gate.SERVER,
                    "publicNetworkAccess": "Disabled",
                    "minimalTlsVersion": "1.2"}

    def _validate(self, **changes):
        supplied = dict(account=self.account, signed_in_owner=self.owner,
                        app=self.app, instances=self.instances, sql_server=self.sql)
        supplied.update(changes)
        # The test never uses real Owner IDs or credentials: only fake digests.
        with patch("p3_ssh_transport_contract.SUB_SHA",
                   gate._hash(self.account["id"])), \
             patch("p3_ssh_transport_contract.TENANT_SHA",
                   gate._hash(self.account["tenantId"])), \
             patch.object(gate, "OWNER_SHA", gate._hash(self.owner["id"])):
            return gate.verify_snapshot(**supplied)

    def test_exact_synthetic_snapshot(self):
        result = self._validate()
        self.assertEqual(result.instance, "instance01234567")
        self.assertEqual(result.sql_server, gate.SERVER)

    def test_multiple_instances_rejected(self):
        with self.assertRaises(gate.SnapshotHold):
            self._validate(instances=self.instances * 2)

    def test_wrong_owner_rejected(self):
        with self.assertRaises(gate.SnapshotHold):
            self._validate(signed_in_owner={"id": "another"})

    def test_public_sql_rejected(self):
        sql = dict(self.sql, publicNetworkAccess="Enabled")
        with self.assertRaises(gate.SnapshotHold):
            self._validate(sql_server=sql)

    def test_wrong_app_or_no_vnet_rejected(self):
        for app in (dict(self.app, name="other"),
                    dict(self.app, virtualNetworkSubnetId=None)):
            with self.subTest(app=app):
                with self.assertRaises(gate.SnapshotHold):
                    self._validate(app=app)


if __name__ == "__main__":
    unittest.main()
