import os
import unittest
from unittest.mock import MagicMock, patch

import server


class TestMtlsConfig(unittest.TestCase):
    def test_configure_ports_insecure_by_default(self):
        srv = MagicMock()
        with patch.dict(os.environ, {}, clear=True):
            server._configure_ports(srv, 50051)
        srv.add_insecure_port.assert_called_once_with("[::]:50051")
        srv.add_secure_port.assert_not_called()

    def test_configure_ports_mtls_missing_paths_raises(self):
        srv = MagicMock()
        with patch.dict(os.environ, {"PYROPE_SIDECAR_MTLS_ENABLED": "true"}, clear=True):
            with self.assertRaises(ValueError):
                server._configure_ports(srv, 50051)

    def test_configure_ports_mtls_uses_secure_port(self):
        srv = MagicMock()
        env = {
            "PYROPE_SIDECAR_MTLS_ENABLED": "true",
            "PYROPE_SIDECAR_CERT_PEM": "/cert.pem",
            "PYROPE_SIDECAR_KEY_PEM": "/key.pem",
            "PYROPE_SIDECAR_CA_CERT_PEM": "/ca.pem",
        }
        with patch.dict(os.environ, env, clear=True):
            with patch("server._read_file_bytes", return_value=b"dummy"):
                with patch("server.grpc.ssl_server_credentials", return_value="creds") as ssl_creds:
                    server._configure_ports(srv, 50051)

        ssl_creds.assert_called_once()
        srv.add_secure_port.assert_called_once_with("[::]:50051", "creds")


if __name__ == "__main__":
    unittest.main()

