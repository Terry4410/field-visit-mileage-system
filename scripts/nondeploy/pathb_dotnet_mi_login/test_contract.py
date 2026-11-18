"""Static NONDEPLOY guard. Never invokes the probe or Azure."""
from pathlib import Path
import unittest

SOURCE = Path(__file__).with_name("Program.cs").read_text(encoding="utf-8")
PROJECT = Path(__file__).with_name("PathB.MiLoginOnce.csproj").read_text(encoding="utf-8")


class FailClosedContract(unittest.TestCase):
    def test_fixed_target(self):
        self.assertIn('sql-fieldvisit-jpe-pathb-uat.database.windows.net', SOURCE)
        self.assertIn('db-fieldvisit-pathb-uat', SOURCE)
        self.assertIn('10.90.2.4', SOURCE)
        self.assertIn('https://database.windows.net/', SOURCE)

    def test_one_token_one_login(self):
        self.assertEqual(SOURCE.count('client.SendAsync('), 1)
        self.assertEqual(SOURCE.count('connection.OpenAsync('), 1)
        self.assertIn('AllowAutoRedirect = false', SOURCE)
        self.assertIn('UseProxy = false', SOURCE)
        self.assertIn('ConnectRetryCount = 0', SOURCE)
        self.assertIn('Pooling = false', SOURCE)
        self.assertIn('WaitAsync(TimeSpan.FromSeconds(5))', SOURCE)
        self.assertIn('deadline.Token', SOURCE)
        self.assertIn('uri.AbsolutePath != "/MSI/token"', SOURCE)
        self.assertIn('!string.IsNullOrEmpty(uri.Query)', SOURCE)

    def test_no_sql_execution_or_ef(self):
        for forbidden in ('ExecuteReader', 'ExecuteNonQuery', 'ExecuteScalar',
                          'SqlCommand(', 'DbContext', 'CanConnectAsync',
                          'CREATE USER', 'GRANT ', 'Task.Delay(', 'Thread.Sleep('):
            self.assertNotIn(forbidden, SOURCE)
        self.assertNotRegex(SOURCE, r'\bfor\s*\(|\bwhile\s*\(.*retry')
        self.assertIn('PASS_NO_SQL_EXECUTED', SOURCE)

    def test_secret_and_exception_suppression(self):
        self.assertNotIn('Console.Error.Write', SOURCE)
        self.assertNotIn('ex.Message', SOURCE)
        self.assertNotIn('ex.ToString', SOURCE)
        self.assertNotIn('Console.WriteLine(token', SOURCE)
        self.assertNotIn('Console.WriteLine(endpoint', SOURCE)
        self.assertIn('HOLD_MI_TOKEN_REQUEST_UNKNOWN', SOURCE)

    def test_framework_reuse(self):
        self.assertIn('<TargetFramework>net8.0</TargetFramework>', PROJECT)
        self.assertIn('<PackageReference Include="Microsoft.Data.SqlClient" Version="5.2.3" />', PROJECT)
        self.assertNotIn('ProjectReference', PROJECT)


if __name__ == '__main__':
    unittest.main(verbosity=2)
