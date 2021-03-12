from hotstorage.hotstorage_model_pb2 import World, CraneSchedule, CraneMove

def crane_schedule(world):
    if len(world.Crane.Schedule.Moves) > 0:
        return None
    schedule = CraneSchedule()
    if len(world.Production.BottomToTop) > 0:
        block = world.Production.BottomToTop[-1]
        for buf in world.Buffers:
            if buf.MaxHeight > len(buf.BottomToTop):
                mov = schedule.Moves.add()
                mov.BlockId = block.Id
                mov.SourceId = world.Production.Id
                mov.TargetId = buf.Id
                return schedule

    return None