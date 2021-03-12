use protoc_rust::Customize;

fn main() {
    protoc_rust::run(protoc_rust::Args {
        out_dir: "src/hotstorage",
        input: &["../hotstorage_model.proto"],
        includes: &[".."],
        customize: Customize {
            ..Default::default()
        },
    })
    .expect("protoc");
    protoc_rust::run(protoc_rust::Args {
        out_dir: "src/rollingmill",
        input: &["../rollingmill_model.proto"],
        includes: &[".."],
        customize: Customize {
            ..Default::default()
        },
    })
    .expect("protoc");
}
