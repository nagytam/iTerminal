! A small FORTRAN program: prints a greeting and the mean of some values.
program hello_totals
    use, intrinsic :: iso_fortran_env, only: dp => real64
    implicit none
    integer, parameter :: n = 5
    real(dp) :: values(n), total, mean
    integer :: i

    print *, "Hello from iTerminal"
    values = [1.5_dp, 2.0d0, 3.25e1, -4.0e-1, 10.0_dp]
    total = 0.0_dp
    do i = 1, n
        total = total + values(i)
    end do
    mean = total / real(n, dp)
    if (mean > 5.0_dp) then
        write(*, '(A, F8.3)') 'Mean is large: ', mean
    else if (mean == 0.0_dp) then
        print *, 'It''s zero'
    else
        write(*, *) 'Mean: ', mean
    end if
end program hello_totals
