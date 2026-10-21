r"""Local, NON-CAR DoIP/UDS simulator for exercising Factory Sound Studio.

Requirements: Python 3.10+ only, no packages. Binds LOOPBACK only, no vehicle access.
On Windows: py -3 tools\mock_doip_vehicle.py
Select the 127.0.0.1 loopback adapter in the GUI, set IPv4 127.0.0.1,
ECU target 0x1001, tester 0E00, then connect and read.
"""
import socket
import struct
import threading

HOST, PORT = "127.0.0.1", 13400
LA = 0x1001  # SIMULATED DoIP entity; not an Audi address
VIN = b"TESTONLYNOTREAL001"  # deliberately synthetic; truncated to 17 bytes

def pack(kind, payload=b""):
    return b"\x02\xfd" + struct.pack(">HI", kind, len(payload)) + payload

def parse(data):
    if len(data) < 8 or data[0:2] != b"\x02\xfd":
        raise ValueError("bad header")
    kind, size = struct.unpack(">HI", data[2:8])
    if size > 131072 or len(data) != 8 + size:
        raise ValueError("bad length")
    return kind, data[8:]

def recv_exact(client, length):
    content = b""
    while len(content) < length:
        piece = client.recv(length - len(content))
        if not piece:
            raise ConnectionError("closed")
        content += piece
    return content

def receive(client):
    hdr = recv_exact(client, 8)
    if hdr[:2] != b"\x02\xfd":
        raise ValueError("bad header")
    kind, size = struct.unpack(">HI", hdr[2:])
    if size > 131072:
        raise ValueError("size too large")
    return kind, recv_exact(client, size)

def udp_server():
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
        sock.bind((HOST, PORT))
        while True:
            data, remote = sock.recvfrom(4096)
            try:
                kind, _ = parse(data)
                if kind == 1:
                    announced = VIN[:17].ljust(17, b"0") + struct.pack(">H", LA) + bytes.fromhex("AABBCCDDEEFF") + bytes(6) + b"\x00\x00"
                    sock.sendto(pack(4, announced), remote)
            except (ValueError, struct.error):
                continue

def tcp_client(client, peer):
    print("TCP connection from", peer)
    with client:
        activated, tester = False, 0x0E00
        while True:
            try:
                kind, payload = receive(client)
                if kind == 5 and len(payload) in (7, 11):
                    tester = struct.unpack(">H", payload[:2])[0]
                    client.sendall(pack(6, struct.pack(">HHB", tester, LA, 0x10) + bytes(4)))
                    activated = True
                elif kind == 0x8001 and activated and len(payload) >= 7:
                    source, target = struct.unpack(">HH", payload[:4])
                    if source != tester or target != LA or payload[4] != 0x22:
                        continue
                    did = struct.unpack(">H", payload[5:7])[0]
                    client.sendall(pack(0x8002, struct.pack(">HHB", LA, tester, 0)))
                    responses = {
                        0xF187: b"SIM-GATEWAY-001", 0xF188: b"SIM-SW-01",
                        0xF189: b"0.1", 0xF18A: b"LOCAL-DEMO",
                        0xF190: VIN[:17].ljust(17, b"0"), 0xF191: b"SIM-HARDWARE"
                    }
                    uds = (b"\x62" + struct.pack(">H", did) + responses[did]
                           if did in responses else b"\x7f\x22\x31")
                    client.sendall(pack(0x8001, struct.pack(">HH", LA, tester) + uds))
            except (OSError, ConnectionError, ValueError):
                break
    print("TCP session ended")

def main():
    threading.Thread(target=udp_server, daemon=True).start()
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as server:
        server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        server.bind((HOST, PORT))
        server.listen(5)
        print("SIMULATOR ONLY: listening on 127.0.0.1:13400, no car connection")
        while True:
            client, peer = server.accept()
            threading.Thread(target=tcp_client, args=(client, peer), daemon=True).start()

if __name__ == "__main__":
    main()
