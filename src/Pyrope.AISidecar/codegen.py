import os
import sys
from grpc_tools import protoc

def generate():
    proto_path = '../Protos'
    out_path = '.'
    
    if not os.path.exists(proto_path):
        print(f"Error: {proto_path} not found.")
        return

    print("Generating GRPC code...")
    protoc.main((
        '',
        f'-I{proto_path}',
        f'--python_out={out_path}',
        f'--grpc_python_out={out_path}',
        'policy_service.proto',
    ))
    print("Done.")

if __name__ == '__main__':
    generate()
