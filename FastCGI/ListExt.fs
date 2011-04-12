module List

let rec skip n xs =
   if n = 0 then xs
   else
      match xs with
      | _::xr -> skip (n-1) xr
      | [] -> []

let rec take n xs acc =
   if n = 0 then List.rev acc
   else
      match xs with
      | x::xr -> take (n-1) xr (x::acc)
      | [] -> List.rev acc

let takeDropWhile pred xs =
   let rec loop ys acc =
      match ys with
      | h::t when pred h -> loop t (h::acc)
      | rest -> List.rev acc, rest
   loop xs []



