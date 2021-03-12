mod rollingmill;
mod hotstorage;

use protobuf::Message;

#[derive(Debug, Copy, Clone)]
pub enum OptimizerType {
    RuleBased,
    ModelBased,
}
#[derive(Debug, Copy, Clone)]
pub enum Problem{
    RollingMill,
    Hotstorage,
}

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let mut args = std::env::args().skip(1);
    let socket_addr = args.next().expect("Expected address of world socket");
    let sim_id = args.next().expect("Expected simulation id");

    let problem = match args.next().expect("Expected problem type (HS, RM)").as_str() {
        "HS" => Problem::Hotstorage,
        "RM" => Problem::RollingMill,
        _ => panic!("Expected problem type (HS, RM)"),
    };

    let opt_type = if args.next().is_some() {
        OptimizerType::ModelBased
    } else {
        OptimizerType::RuleBased
    };
    println!("{:?} {:?}", problem, opt_type);

    let ctx = zmq::Context::new();
    let socket = ctx.socket(zmq::DEALER)?;
    socket.set_identity(sim_id.as_bytes())?;
    socket.connect(&socket_addr)?;
    println!("Connected");
    while let Ok(msg) = socket.recv_multipart(0) {
        //println!("{:?}",msg);
        let response = match problem {
            Problem::Hotstorage => {
                if let Some(new_schedule) = hotstorage::plan_moves(&protobuf::parse_from_bytes(&msg[2])?, opt_type) {
                    Some(new_schedule.write_to_bytes()?)
                } else {
                    None
                }
            }
            Problem::RollingMill => {
                if let Some(new_plan) = rollingmill::plan_moves(&protobuf::parse_from_bytes(&msg[2])?) {
                    Some(new_plan.write_to_bytes()?)
                } else {
                    None
                }
            }
        };
        
        if let Some(message) = response{
            println!("send");
            socket.send_multipart(vec![Vec::new(), "crane".as_bytes().to_vec(), message], 0)?;
        }

    }
    Ok(())
}
