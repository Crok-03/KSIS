import os
import datetime
import shutil
from flask import Flask, request, jsonify, send_file, abort, make_response
from werkzeug.utils import safe_join

app = Flask(__name__)

UPLOAD_FOLDER = os.path.join(os.getcwd(), 'storage')
os.makedirs(UPLOAD_FOLDER, exist_ok=True)
app.config['UPLOAD_FOLDER'] = UPLOAD_FOLDER

def get_full_path(rel_path):
    try:
        return safe_join(app.config['UPLOAD_FOLDER'], rel_path)
    except:
        abort(400)

@app.route('/<path:rel_path>', methods=['PUT'])
def upload_file(rel_path):
    full_path = get_full_path(rel_path)
    os.makedirs(os.path.dirname(full_path), exist_ok=True)
    
    with open(full_path, 'wb') as f:
        f.write(request.get_data())
    return ('', 201)

@app.route('/<path:rel_path>', methods=['GET'])
def get_file_or_dir(rel_path):
    full_path = get_full_path(rel_path)
    
    if os.path.isdir(full_path):
        items = [{
            'name': n, 
            'type': 'dir' if os.path.isdir(os.path.join(full_path, n)) else 'file'
        } for n in os.listdir(full_path)]
        
        return jsonify(items), 200
    elif os.path.isfile(full_path):
        return send_file(full_path)
    else:
        abort(404)

@app.route('/<path:rel_path>', methods=['HEAD'])
def head_file(rel_path):
    full_path = get_full_path(rel_path)
    
    if not os.path.isfile(full_path):
        abort(404)
        
    st = os.stat(full_path)
    resp = make_response()
    resp.headers['Content-Length'] = st.st_size
    resp.headers['Last-Modified'] = datetime.datetime.utcfromtimestamp(st.st_mtime).strftime('%a, %d %b %Y %H:%M:%S GMT')
    return resp

@app.route('/<path:rel_path>', methods=['DELETE'])
def delete_path(rel_path):
    full_path = get_full_path(rel_path)
    
    if os.path.isfile(full_path):
        os.remove(full_path)
        return '', 204
    elif os.path.isdir(full_path):
        shutil.rmtree(full_path)
        return '', 204
    else:
        abort(404)

if __name__ == '__main__':
    app.run(host='0.0.0.0', port=5000)